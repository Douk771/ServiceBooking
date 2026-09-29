using ServiceBooking.API.Startup;

// Cycle 22 P5 (ARCHITECTURE_CYCLE22.md §378): every section of this file moved, verbatim, into
// Startup/*Extensions.cs; this is the same sequence of registrations and middleware, in the same order.
var builder = WebApplication.CreateBuilder(args);

builder.AddServiceBookingSerilog();
var isDeveloperEnvironment = builder.ValidateDeployment();
builder.AddServiceBookingControllers();
builder.AddServiceBookingDatabase();
builder.AddAuth();
builder.AddServiceBookingCors();
builder.AddServiceBookingApplicationServices();
builder.AddNotifications();
builder.AddAddressVerification();
builder.AddPhoneVerification();
builder.AddServiceBookingForwardedHeaders();
builder.AddServiceBookingRateLimiting();
builder.AddServiceBookingHealthChecks();
builder.AddBackgroundTasks();

var app = builder.Build();

app.ValidateServiceRegistries();

// FIRST in the pipeline, before anything reads Connection.RemoteIpAddress — the rate limiter's IP
// partitions (auth-login, auth-register, booking-create) and Serilog's request logging both need the
// REAL client address, not nginx's, and both run later in this pipeline (ARCHITECTURE.md §9.1).
app.UseForwardedHeaders();

app.ValidateDeploymentAfterBuild(builder, isDeveloperEnvironment);
app.UseServiceBookingRequestLogging();
app.UseServiceBookingExceptionHandler();
app.UseServiceBookingSwagger();
app.UseCors();
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
