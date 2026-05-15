using Runlet.Persistence;
using Runlet.Worker;
using Runlet.Worker.Claiming;
using Runlet.Worker.Execution;
using Runlet.Worker.Heartbeats;
using Runlet.Worker.Lifecycle;
using Runlet.Worker.Logging;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddRunletPersistence(builder.Configuration);
builder.Services.AddSingleton<LocalShellWorkflowStepExecutor>();
builder.Services.AddSingleton<DockerWorkflowStepExecutor>();
builder.Services.AddSingleton<IWorkflowStepExecutorFactory, WorkflowStepExecutorFactory>();
builder.Services.AddSingleton<WorkflowLogWriter>();
builder.Services.AddSingleton<WorkflowRunFinalizer>();
builder.Services.AddSingleton<WorkflowRunClaimer>();
builder.Services.AddSingleton<WorkflowRunHeartbeat>();
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
await host.RunAsync();
