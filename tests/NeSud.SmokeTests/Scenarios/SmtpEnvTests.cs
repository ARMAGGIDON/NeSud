using Microsoft.Extensions.Configuration;
using NeSud.Server;

namespace NeSud.SmokeTests;

internal static partial class SmokeTests
{
    private static void TestSmtpEnv()
    {
        string root = Path.Combine(Path.GetTempPath(), "NeSud-env-" + Guid.NewGuid().ToString("N"));
        string server = Path.Combine(root, "src", "NeSud.Server");
        Directory.CreateDirectory(server);
        try
        {
            File.WriteAllText(Path.Combine(root, "NeSud.slnx"), "");
            IConfiguration Read()
            {
                var builder = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Smtp:Host"] = "fallback.example.test",
                    ["Smtp:Port"] = "587",
                    ["Smtp:User"] = "fallback-user",
                    ["Database"] = "unchanged"
                });
                SmtpEnv.AddTo(builder, server);
                return builder.Build();
            }

            Check(Read()["Smtp:Host"] == "fallback.example.test", "SMTP configuration fallback");
            File.WriteAllText(Path.Combine(root, ".env"), """
                # SMTP configuration
                SMTP_HOST=root.example.test # comment
                Smtp__Password='p=a# ss'
                export SMTP_FROM="mail@example.test" # comment
                SMTP_USER=
                Database=ignored
                """);
            var config = Read();
            Check(config["Smtp:Host"] == "root.example.test" && config["Smtp:Password"] == "p=a# ss" &&
                config["Smtp:From"] == "mail@example.test" && config["Smtp:Port"] == "587" &&
                config["Smtp:User"] == "" && config["Database"] == "unchanged", "SMTP dotenv precedence and parsing");

            string local = Path.Combine(server, ".env");
            File.WriteAllText(local, "Smtp__Host=local.example.test\nSmtp__Password=abc#def");
            config = Read();
            Check(config["Smtp:Host"] == "local.example.test" && config["Smtp:Password"] == "abc#def" &&
                config["Smtp:From"] is null && config["Smtp:User"] == "fallback-user", "ContentRoot dotenv wins");

            File.WriteAllText(local, "SMTP_PASSWORD='unterminated-secret");
            bool rejected = false;
            try { Read(); }
            catch (FormatException ex) { rejected = !ex.Message.Contains("unterminated-secret"); }
            Check(rejected, "Malformed dotenv fails without leaking secrets");
            Console.WriteLine("PASS SMTP dotenv precedence, fallback, parsing and secret-safe diagnostics");
        }
        finally { Directory.Delete(root, true); }
    }
}
