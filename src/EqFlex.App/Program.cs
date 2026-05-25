using System;
using System.IO;
using Serilog;
using Velopack;

namespace EqFlex.App;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        var logDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "EqFlex", "logs");

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Warning()
            .WriteTo.File(
                Path.Combine(logDir, "eqflex-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        try
        {
            VelopackApp.Build().Run();
            var app = new App();
            app.InitializeComponent();
            app.Run();
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Unhandled exception at startup");
            throw;
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }
}
