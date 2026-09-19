using ParentalGuard.Watchdog;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
builder.Services.AddWindowsService(options => options.ServiceName = "ParentalGuardWatchdog"); // ANTI-010, Architecture/09 mục 3.1
builder.Services.AddSingleton(new StartupArgs(args));
builder.Services.AddHostedService<Worker>();

IHost host = builder.Build();
host.Run();
