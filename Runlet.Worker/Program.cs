using Runlet.Persistence;
using Runlet.Worker;
using Runlet.Worker.Execution;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddRunletPersistence(builder.Configuration);
builder.Services.AddSingleton<IWorkflowStepExecutor, LocalShellWorkflowStepExecutor>();
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
await host.RunAsync();
