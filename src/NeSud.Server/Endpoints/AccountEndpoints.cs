using NeSud.Core;

namespace NeSud.Server;

internal static partial class Endpoints
{
    public static void MapAccountEndpoints(this WebApplication app)
    {
        app.MapPost("/api/register", (Credentials c, ServerStore s) => Auth(c, s, true)).RequireRateLimiting("auth");
        app.MapPost("/api/login", (Credentials c, ServerStore s) => Auth(c, s, false)).RequireRateLimiting("auth");
        app.MapPost("/api/guest", (ServerStore s) => s.Guest() is { } guest
            ? Results.Ok(guest) : Results.Problem("Демонстраційні сеанси зайняті. Спробуйте пізніше.", statusCode: 503)).RequireRateLimiting("auth");
        app.MapGet("/api/health", () => Results.Ok(new SyncAck(true)));
        app.MapPost("/api/logout", (HttpContext c, ServerStore s) =>
        {
            s.Logout(Token(c));
            return Results.Ok();
        });
    }

    static string Token(HttpContext context)
    {
        var value = context.Request.Headers.Authorization.ToString();
        return value.StartsWith("Bearer ", StringComparison.Ordinal) ? value[7..] : "";
    }
    static string? User(HttpContext context, ServerStore store) => store.Authenticate(Token(context));
    static IResult Auth(Credentials c, ServerStore store, bool register)
    {
        if (ServerStore.ValidateCredentials(c, register) is { } error) return Results.BadRequest(new { error });
        var session = store.Login(c, register);
        return session is not null ? Results.Ok(session) : register
            ? Results.Conflict(new { error = "Цей логін уже зайнятий." })
            : Results.Json(new { error = "Неправильний логін або пароль." }, statusCode: 401);
    }
}
