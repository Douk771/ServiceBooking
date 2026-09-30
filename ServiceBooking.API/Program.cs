using ServiceBooking.API.Services.Ops;
using ServiceBooking.API.Startup;

// Cycle 22 P5 (ARCHITECTURE_CYCLE22.md §378): every section of this file moved, verbatim, into
// Startup/*Extensions.cs; this is the same sequence of registrations and middleware, in the same order.
// Cycle 28 (ARCHITECTURE_CYCLE28.md §575.1): `dotnet ServiceBooking.API.dll ops <command>` is an operator command run inside the same
// image; the ops words are not host arguments, everything else starts the web server exactly as before.
var opsCommand = OpsCommandLine.Parse(args);
var builder = WebApplication.CreateBuilder(opsCommand?.HostArgs ?? args);

builder.AddServiceBookingSerilog(opsMode: opsCommand is not null);
var isDeveloperEnvironment = builder.ValidateDeployment();
builder.AddServiceBookingControllers();
builder.AddServiceBookingDatabase();
builder.AddAuth();
builder.AddServiceBookingCors();
builder.AddServiceBookingApplicationServices();
builder.AddNotifications();
builder.AddPhoneVerification();
builder.AddServiceBookingForwardedHeaders();
builder.AddServiceBookingRateLimiting();
builder.AddServiceBookingHealthChecks();
builder.AddBackgroundTasks();

var app = builder.Build();

app.ValidateServiceRegistries();

if (opsCommand is not null)
{
    // No middleware, no background tasks, no seeding: only the command (§575.1).
    Environment.ExitCode = await OpsCommandRunner.RunAsync(app.Services, opsCommand, Console.Out);
    await app.DisposeAsync();
    return;
}

// FIRST in the pipeline, before anything reads Connection.RemoteIpAddress — the rate limiter's IP
// partitions (auth-login, auth-register, booking-create) and Serilog's request logging both need the
// REAL client address, not nginx's, and both run later in this pipeline (ARCHITECTURE.md §9.1).
app.UseForwardedHeaders();

// Cycle 28, pass B (ARCHITECTURE_CYCLE28.md §579.3, §580): the demo's noindex header middleware, FIRST after the forwarded headers — it must mark every answer,
// including the maintenance 503 and the ones of the exception handler. A no-op unless DemoMode:Enabled. Its pair, the maintenance middleware, is after UseCors below.
app.UseMiddleware<ServiceBooking.API.Services.Demo.DemoResponseHeadersMiddleware>();

app.ValidateDeploymentAfterBuild(builder, isDeveloperEnvironment);
app.UseServiceBookingRequestLogging();
app.UseServiceBookingExceptionHandler();
app.UseServiceBookingSwagger();
app.UseCors();
// The maintenance answer (503 «Демо обновляется») comes after UseCors, so a cross-origin dev SPA can read it and its X-Demo-Resetting / Retry-After headers, and
// before anything that reads the database (authentication, controllers): the reset holds locks on its tables. No-op unless DemoMode:Enabled.
app.UseMiddleware<ServiceBooking.API.Services.Demo.DemoMaintenanceMiddleware>();
app.UseServiceBookingPublicUploads();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter(); // no global limiter configured — a no-op except where [EnableRateLimiting] is used
app.MapControllers();
app.MapServiceBookingHealthChecks();

await app.SeedAsync(builder);

app.Run();

// Exposes the implicit Program class so the functional test project can spin up
// the app in-memory via WebApplicationFactory<Program>.
public partial class Program;
