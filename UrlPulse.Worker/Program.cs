using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using UrlPulse.Core.Data;
using UrlPulse.Core.Interfaces;
using UrlPulse.Core.Services;

var host = new HostBuilder()
    // This is the "Magic" extension method that handles the 
    // ASP.NET Core integration correctly for Isolated Workers.
    .ConfigureFunctionsWebApplication()
    .ConfigureServices((context, services) =>
    {
        // 1. Get Connection String
        var connectionString = Environment.GetEnvironmentVariable("PostgresConnectionString");

        // 2. Register PostgreSQL (Matches your Core DbContext)
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseNpgsql(connectionString));

        // 3. Register your UrlChecker
        services.AddHttpClient<IUrlChecker, UrlChecker>();
    })
    .Build();

host.Run();