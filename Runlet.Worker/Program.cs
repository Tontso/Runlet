using Runlet.Persistence;
using Runlet.Worker;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddRunletPersistence(builder.Configuration);
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
await host.RunAsync();
