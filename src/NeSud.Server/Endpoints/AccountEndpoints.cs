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
        app.MapPost("/api/account/settings", (HttpContext c, ServerStore s, ProfileEdit edit) =>
            User(c, s) is not { } name ? Results.Unauthorized() :
            !s.IsVerified(name) && !string.Equals(edit.Username?.Trim(), s.Settings(name).Username, StringComparison.Ordinal) ? VerificationRequired() :
            s.EditProfile(name, edit) ? Results.Ok(s.Settings(name)) : Results.BadRequest(new { error = "Некоректний або зайнятий нікнейм/email. Підтверджений email змінити не можна." }));
        app.MapPost("/api/account/verification/send", async (HttpContext c, ServerStore s, IConfiguration config) =>
        {
            if (User(c, s) is not { } name) return Results.Unauthorized();
            if (string.IsNullOrWhiteSpace(config["Smtp:Host"]) || string.IsNullOrWhiteSpace(config["Smtp:From"]))
                return Results.Problem("SMTP не налаштовано.", statusCode: 503);
            var challenge = s.StartVerification(name);
            if (challenge is null) return Results.BadRequest(new { error = "Немає пошти, вона вже підтверджена або не минула хвилина після попереднього запиту." });
            try
            {
                using var client = new System.Net.Mail.SmtpClient(config["Smtp:Host"]!, config.GetValue("Smtp:Port", 587));
                client.EnableSsl = true;
                if (!string.IsNullOrEmpty(config["Smtp:User"]))
                    client.Credentials = new System.Net.NetworkCredential(config["Smtp:User"], config["Smtp:Password"]);
                using var mail = new System.Net.Mail.MailMessage(config["Smtp:From"]!, challenge.Value.Email,
                    "NeSud: підтвердження пошти", "Код підтвердження: " + challenge.Value.Code + "\nДіє 30 хвилин.");
                await client.SendMailAsync(mail, c.RequestAborted);
                return Results.Ok(new SyncAck(true));
            }
            catch (Exception) { return Results.Problem("Не вдалося надіслати лист.", statusCode: 503); }
        }).RequireRateLimiting("auth");
        app.MapPost("/api/account/verification", (HttpContext c, ServerStore s, VerificationRequest request) =>
            User(c, s) is not { } name ? Results.Unauthorized() :
            s.VerifyEmail(name, request.Code) ? Results.Ok(s.Settings(name)) :
            Results.BadRequest(new { error = "Код недійсний або прострочений." })).RequireRateLimiting("auth");
        app.MapPost("/api/account/avatar", (HttpContext c, ServerStore s, AvatarEdit edit) =>
            User(c, s) is not { } name ? Results.Unauthorized() :
            !s.IsVerified(name) ? VerificationRequired() :
            s.SetAvatar(name, edit.Png) ? Results.Ok(new SyncAck(true)) : Results.BadRequest(new { error = "Некоректне квадратне PNG-зображення." }));
        app.MapGet("/api/avatar", (ServerStore s, string user) =>
            s.GetAvatar(user) is { } png ? Results.File(png, "image/png") : Results.NotFound());
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
