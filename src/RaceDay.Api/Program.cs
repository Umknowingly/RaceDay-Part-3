using System.Reflection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using RaceDay.Api.Data;
using RaceDay.Api.Services;

var builder = WebApplication.CreateBuilder(args);

// --- Database ---
// A relative Data Source such as "raceday.db" is placed in the project folder, so the same
// file is used however the API is started. A full path (for example in Docker) is used as given.
var sqlite = new SqliteConnectionStringBuilder(builder.Configuration.GetConnectionString("RaceDayDb"));
if (!Path.IsPathRooted(sqlite.DataSource) && sqlite.DataSource != ":memory:")
{
    sqlite.DataSource = Path.Combine(builder.Environment.ContentRootPath, sqlite.DataSource);
}

builder.Services.AddDbContext<RaceDayDbContext>(options => options.UseSqlite(sqlite.ConnectionString));

// --- Password hashing ---
builder.Services.AddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();

// --- Server-side session: stores the logged-in user's ID and role ---
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.Cookie.Name = ".RaceDay.Session";
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.IdleTimeout = TimeSpan.FromMinutes(30);
});

builder.Services.AddControllers();

// --- Swagger / OpenAPI ---
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "RaceDay API",
        Version = "v1",
        Description = "Event management API for South African road running, walking and cycling events. " +
                      "Log in through /api/auth/login first; the session cookie is then sent automatically by Swagger UI."
    });

    var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
    if (File.Exists(xmlPath))
    {
        options.IncludeXmlComments(xmlPath);
    }
});

var app = builder.Build();

// Create or update raceday.db from the EF Core migrations when the API starts.
// (Skipped in the test environment, which uses an in-memory database.)
if (!app.Environment.IsEnvironment("Testing"))
{
    using var scope = app.Services.CreateScope();
    scope.ServiceProvider.GetRequiredService<RaceDayDbContext>().Database.Migrate();
}

app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "RaceDay API v1");
    options.RoutePrefix = "swagger";
});

if (!app.Environment.IsEnvironment("Testing"))
{
    app.UseHttpsRedirection();
}

app.UseSession();
app.MapControllers();

app.Run();

// Exposed so the test project can host the API with WebApplicationFactory<Program>.
public partial class Program { }
