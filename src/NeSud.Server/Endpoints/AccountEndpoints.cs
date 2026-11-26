using Microsoft.AspNetCore.SignalR;
using NeSud.Core;

namespace NeSud.Server;

internal static partial class Endpoints
{
    public static void MapAccountEndpoints(this WebApplication app)
    {
        app.MapPost("/api/register", (Credentials c, ServerStore s) => string.IsNullOrWhiteSpace(c.Email) ? Results.BadRequest(new { error = "Електронна пошта обов’язкова." }) : Auth(c, s, true)).RequireRateLimiting("auth");
        app.MapPost("/api/login", (Credentials c, ServerStore s) => Auth(c, s, false)).RequireRateLimiting("auth");
        app.MapPost("/api/guest", (ServerStore s) => s.Guest() is { } guest
            ? Results.Ok(guest) : Results.Problem("Демонстраційні сеанси зайняті. Спробуйте пізніше.", statusCode: 503)).RequireRateLimiting("auth");
        app.MapGet("/api/account/settings", (HttpContext c, ServerStore s) =>
            User(c, s) is { } name ? Results.Ok(s.Settings(name)) : Results.Unauthorized());
        app.MapPost("/api/account/settings", async (HttpContext c, ServerStore s, IHubContext<GameHub> hub, ProfileEdit edit) =>
        {
            if (User(c, s) is not { } name) return Results.Unauthorized();
            if (!s.IsVerified(name) && !string.Equals(edit.Username?.Trim(), s.Settings(name).Username, StringComparison.Ordinal))
                return VerificationRequired();
            if (!s.EditProfile(name, edit))
                return Results.BadRequest(new { error = "Некоректний або зайнятий нікнейм/email. Підтверджений email змінити не можна." });
            await GameHub.NotifyAccount(hub.Clients.Group(GameHub.Group(name)));
            await GameHub.NotifyRankings(hub.Clients.Group(GameHub.RankingsGroup));
            return Results.Ok(s.Settings(name));
        });
        app.MapPost("/api/account/verification/send", async (HttpContext c, ServerStore s, IConfiguration config) =>
        {
            if (User(c, s) is not { } name) return Results.Unauthorized();
            if (string.IsNullOrWhiteSpace(config["Smtp:Host"]) || string.IsNullOrWhiteSpace(config["Smtp:From"]))
                return Results.Problem("SMTP не налаштовано.", statusCode: 503);
            var challenge = s.StartVerification(name);
            if (challenge is null) return Results.BadRequest(new { error = "Немає пошти, вона вже підтверджена або не минула хвилина після попереднього запиту." });
            try
            {
                int port = config.GetValue("Smtp:Port", 587);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(c.RequestAborted);
                timeout.CancelAfter(TimeSpan.FromSeconds(20));
                using var client = new MailKit.Net.Smtp.SmtpClient();
                var security = port == 465 ? MailKit.Security.SecureSocketOptions.SslOnConnect
                    : MailKit.Security.SecureSocketOptions.StartTls;
                var mail = new MimeKit.MimeMessage();
                mail.From.Add(new MimeKit.MailboxAddress("NeSud", config["Smtp:From"]!));
                mail.To.Add(MimeKit.MailboxAddress.Parse(challenge.Value.Email));
                mail.Subject = "NeSud: підтвердження пошти";
                mail.Body = new MimeKit.TextPart("plain")
                {
                    Text = "Код підтвердження: " + challenge.Value.Code + "\nДіє 30 хвилин."
                };
                await client.ConnectAsync(config["Smtp:Host"]!, port, security, timeout.Token);
                if (!string.IsNullOrEmpty(config["Smtp:User"]))
                    await client.AuthenticateAsync(config["Smtp:User"]!, config["Smtp:Password"] ?? "", timeout.Token);
                await client.SendAsync(new MailKit.SendRequest(mail), timeout.Token);
                // Delivery succeeded; failure to send QUIT must not report it as unsent.
                try { await client.DisconnectAsync(true, timeout.Token); } catch { }
                return Results.Ok(new SyncAck(true));
            }
            catch (Exception) { return Results.Problem("Не вдалося надіслати лист.", statusCode: 503); }
        }).RequireRateLimiting("auth");
        app.MapPost("/api/account/verification", async (HttpContext c, ServerStore s, IHubContext<GameHub> hub, VerificationRequest request) =>
        {
            if (User(c, s) is not { } name) return Results.Unauthorized();
            if (!s.VerifyEmail(name, request.Code)) return Results.BadRequest(new { error = "Код недійсний або прострочений." });
            await GameHub.NotifyAccount(hub.Clients.Group(GameHub.Group(name)));
            if (s.RewardedInviter(name) is { } inviter)
                await GameHub.NotifyAccount(hub.Clients.Group(GameHub.Group(inviter)));
            return Results.Ok(s.Settings(name));
        }).RequireRateLimiting("auth");
        app.MapPost("/api/account/avatar", async (HttpContext c, ServerStore s, IHubContext<GameHub> hub, AvatarEdit edit) =>
        {
            if (User(c, s) is not { } name) return Results.Unauthorized();
            if (!s.IsVerified(name)) return VerificationRequired();
            if (edit.Png is null) return Results.BadRequest(new { error = "Аватар можна лише замінити." });
            if (!s.SetAvatar(name, edit.Png)) return Results.BadRequest(new { error = "Некоректне квадратне PNG-зображення." });
            await GameHub.NotifyAccount(hub.Clients.Group(GameHub.Group(name)));
            return Results.Ok(new SyncAck(true));
        });
        app.MapGet("/api/avatar", (HttpContext c, ServerStore s, string user) =>
        {
            c.Response.Headers.CacheControl = "public, max-age=60";
            return s.GetAvatar(user) is { } png ? Results.File(png, "image/png") : Results.NotFound();
        });
        app.MapGet("/api/health", () => Results.Ok(new SyncAck(true)));
        app.MapPost("/api/logout", (HttpContext c, ServerStore s) =>
        {
            s.Logout(Token(c));
            return Results.Ok();
        });
    }

    private static IResult VerificationRequired() =>
        Results.Json(new { error = "Спочатку підтвердьте електронну пошту." }, statusCode: 403);

    private static string Token(HttpContext context)
    {
        var value = context.Request.Headers.Authorization.ToString();
        return value.StartsWith("Bearer ", StringComparison.Ordinal) ? value[7..] : "";
    }
    private static string? User(HttpContext context, ServerStore store) => store.Authenticate(Token(context));
    private static IResult Auth(Credentials c, ServerStore store, bool register)
    {
        if (ServerStore.ValidateCredentials(c, register) is { } error) return Results.BadRequest(new { error });
        var session = store.Login(c, register);
        return session is not null ? Results.Ok(session) : register
            ? Results.Conflict(new { error = "Цей нікнейм або email уже зайнятий." })
            : Results.Json(new { error = "Неправильний логін або пароль." }, statusCode: 401);
    }
}
