using ParentalGuard.Service;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
builder.Services.AddWindowsService(options => options.ServiceName = "ParentalGuardService"); // BE-010: Automatic, chạy trước đăng nhập
builder.Services.AddHostedService<Worker>();

IHost host = builder.Build();
host.Run();
