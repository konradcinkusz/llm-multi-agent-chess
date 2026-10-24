var builder = DistributedApplication.CreateBuilder(args);

builder.AddProject<Projects.Chess_Api>("api");
builder.AddProject<Projects.Chess_Web>("web");

builder.Build().Run();
