using Server;

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;

var errors = new List<string>();

var dataPath = config["data-path"];
if (string.IsNullOrWhiteSpace(dataPath))
  errors.Add("--data-path is required");

ValidatePort(config["http-port"], "--http-port", errors);
ValidatePort(config["quic-port"], "--quic-port", errors);

if (config["bind"] is { } bindStr && !System.Net.IPAddress.TryParse(bindStr, out _))
  errors.Add($"--bind must be a valid IP address, got '{bindStr}'");

if (errors.Count > 0)
{
  Console.Error.WriteLine("Error:");
  foreach (var error in errors)
    Console.Error.WriteLine($"  {error}");

  Console.Error.WriteLine();
  Console.Error.WriteLine("Usage: server --data-path <path> [options]");
  Console.Error.WriteLine();
  Console.Error.WriteLine("Options:");
  Console.Error.WriteLine("  --data-path <path>   Root path for persistent data (certs, plugins)");
  Console.Error.WriteLine("  --http-port <port>   TCP port for HTTP web UI and enrollment (default: 8080)");
  Console.Error.WriteLine("  --quic-port <port>   UDP port for QUIC client connections (default: 443)");
  Console.Error.WriteLine("  --bind <address>     Bind address (default: 0.0.0.0)");
  return 1;
}

AppSetup.Configure(builder);
var app = builder.Build();
await AppSetup.InitializeAsync(app);
app.Run();
return 0;

static void ValidatePort(string? value, string name, List<string> errors)
{
  if (value == null) return;
  if (!int.TryParse(value, out var port))
    errors.Add($"{name} must be an integer, got '{value}'");
  else if (port is < 0 or > 65535)
    errors.Add($"{name} must be 0-65535, got '{port}'");
}
