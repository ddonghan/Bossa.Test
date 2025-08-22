using Bossa.Test.HttpApi.Services;
var builder = WebApplication.CreateBuilder(args);
//Leaderboard data needs to be persisted across requests
builder.Services.AddSingleton<LeaderboardService>();
builder.Services.AddControllers();
builder.Services.AddOpenApi();
var app = builder.Build();
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}
app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();
app.Run();
