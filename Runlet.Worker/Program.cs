using Runlet.Persistence;
using Runlet.Worker;
using Runlet.Worker.Cancellation;
using Runlet.Worker.Claiming;
using Runlet.Worker.Execution;
using Runlet.Worker.Heartbeats;
using Runlet.Worker.Lifecycle;
using Runlet.Worker.Logging;
using Runlet.Worker.Registry;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.Configure<WorkerOptions>(builder.Configuration.GetSection("Runlet:Worker"));
builder.Services.AddRunletPersistence(builder.Configuration);
builder.Services.AddSingleton<LocalShellWorkflowStepExecutor>();
builder.Services.AddSingleton<DockerWorkflowStepExecutor>();
builder.Services.AddSingleton<IWorkflowStepExecutorFactory, WorkflowStepExecutorFactory>();
builder.Services.AddSingleton<WorkflowLogWriter>();
builder.Services.AddSingleton<WorkflowRunFinalizer>();
builder.Services.AddSingleton<WorkflowRunClaimer>();
builder.Services.AddSingleton<WorkflowRunHeartbeat>();
builder.Services.AddSingleton<WorkflowRunCancellationWatcher>();
builder.Services.AddSingleton<WorkerRegistry>();
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
await host.RunAsync();
