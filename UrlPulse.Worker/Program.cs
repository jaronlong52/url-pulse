using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration; // Required for GetConnectionString
using Microsoft.EntityFrameworkCore;
using UrlPulse.Core.Data;
using UrlPulse.Core.Interfaces;
using UrlPulse.Core.Services;
using UrlPulse.Worker.Services;

var host = new HostBuilder()
    .ConfigureFunctionsWebApplication()
    .ConfigureAppConfiguration((context, builder) =>
    {
        // This ensures User Secrets are loaded when running locally
        if (context.HostingEnvironment.IsDevelopment())
        {
            builder.AddUserSecrets<Program>();
        }
    })
    .ConfigureServices((context, services) =>
    {
        // Use context.Configuration to grab the string from Secrets or local.settings.json
        var connectionString = context.Configuration.GetConnectionString("DefaultConnection");

        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseNpgsql(connectionString));

        services.AddHttpClient<IUrlChecker, UrlChecker>();

        services.AddSingleton<ICurrentUserService, SystemUserService>();
    })
    .Build();

host.Run();