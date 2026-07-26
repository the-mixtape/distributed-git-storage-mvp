using DistributedGitStorage.StorageNode.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<GitRepositoryStore>();
builder.Services.AddControllers();
builder.Services.AddHealthChecks();

var app = builder.Build();

app.MapControllers();
app.MapHealthChecks("/health");

app.Run();

public partial class Program;
