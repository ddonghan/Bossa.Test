using Bossa.Test.HttpApi.Services;
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<IScoreboardService, OptimizedScoreboardService>();
builder.Services.AddControllers();
var app = builder.Build();
app.MapControllers();
app.Run();