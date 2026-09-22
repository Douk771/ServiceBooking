using ServiceBooking.TestKit;

var command = args.Length > 0 ? args[0] : "status";
var rest = args.Skip(1).ToArray();

return command switch
{
    "status" => await EnvStatus.RunAsync(rest),
    "sweep" => await Sweeper.RunAsync(rest),
    "doctor" => await EnvStatus.RunDoctorAsync(rest),
    _ => PrintUsage(),
};

static int PrintUsage()
{
    Console.Error.WriteLine("Usage: dotnet run --project ServiceBooking.TestKit -- status|sweep|doctor [options]");
    return 1;
}
