using InstantTraceViewer.Server.Services;
using InstantTraceViewer;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();

// Add TraceManager as a singleton service
builder.Services.AddSingleton<TraceManager>();

// Add SimpleTraceFilter as a singleton service
builder.Services.AddSingleton<SimpleTraceFilter>();

var app = builder.Build();

// Configure the HTTP request pipeline.
app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

app.Run();
