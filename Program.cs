using Microsoft.AspNetCore.Mvc;
using System.Threading.Channels;
using BackgroundQueueService;
using Microsoft.Extensions.Primitives;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Cors;

var builder = WebApplication.CreateBuilder(args);
// The max memory to use for the upload endpoint on this instance.
var maxMemory = 500 * 1024 * 1024;

// The max size of a single message, staying below the default LOH size of 85K.
var maxMessageSize = 80 * 1024;

// The max size of the queue based on those restrictions
var maxQueueSize = maxMemory / maxMessageSize;

// Create a channel to send data to the background queue.
builder.Services.AddSingleton<Channel<ReadOnlyMemory<byte>>>((_) =>
    Channel.CreateBounded<ReadOnlyMemory<byte>>(maxQueueSize));

// Create a background queue service.
builder.Services.AddHostedService<BackgroundQueue>();

// Add the memory cache services.
builder.Services.AddMemoryCache();

// Add a custom scoped service.
// builder.Services.AddScoped<ITodoRepository, TodoRepository>();

// Added as service
builder.Services.AddSingleton<Service>();

// builder.Services.AddSingleton<IDateTime, SystemDateTime>();
/*
// Configure JSON options.
builder.Services.Configure<JsonOptions>(options =>
{
    options.SerializerOptions.IncludeFields = true;
});*/

builder.Services.AddAuthorization(o => o.AddPolicy("AdminsOnly", 
                                  b => b.RequireClaim("admin", "true")));

const string MyAllowSpecificOrigins = "_myAllowSpecificOrigins";

builder.Services.AddCors(options =>
{
    options.AddPolicy(name: MyAllowSpecificOrigins,
                      builder =>
                      {
                          builder.WithOrigins("http://example.com",
                                              "http://www.contoso.com");
                      });
});

/*
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options .UseSqlServer(connectionString));
builder.Services.AddDatabaseDeveloperPageExceptionFilter();

builder.Services.AddDefaultIdentity<IdentityUser>(options => options.SignIn.RequireConfirmedAccount = true)
    .AddEntityFrameworkStores<ApplicationDbContext>();*/

var app = builder.Build();

app.UseAuthorization();

// curl --request POST 'https://localhost:<port>/register' --header 'Content-Type: application/json' --data-raw '{ "Name":"Samson", "Age": 23, "Country":"Nigeria" }'
// curl --request POST "https://localhost:<port>/register" --header "Content-Type: application/json" --data-raw "{ \"Name\":\"Samson\", \"Age\": 23, \"Country\":\"Nigeria\" }"
app.MapPost(
    "/register",
    async (
        HttpRequest req,
        Stream body,
        Channel<ReadOnlyMemory<byte>> queue) =>
    {
    if (req.ContentLength is not null && req.ContentLength > maxMessageSize)
    {
    return Results.BadRequest();
    }

    // We're not above the message size and we have a content length, or
    // we're a chunked request and we're going to read up to the maxMessageSize + 1. 
    // We add one to the message size so that we can detect when a chunked request body
    // is bigger than our configured max.
    var readSize = (int?)req.ContentLength ?? (maxMessageSize + 1);

    var buffer = new byte[readSize];

    // Read at least that many bytes from the body.
    // var read = await body.ReadAtLeastAsync(buffer, readSize, throwOnEndOfStream: false);
    var read = await body.ReadAsync(buffer, readSize, 1000);

    // We read more than the max, so this is a bad request.
    if (read > maxMessageSize)
    {
    return Results.BadRequest();
    }

    // Attempt to send the buffer to the background queue.
    if (queue.Writer.TryWrite(buffer.AsMemory(0..read)))
    {
    return Results.Accepted();
    }

    // We couldn't accept the message since we're overloaded.
    return Results.StatusCode(StatusCodes.Status429TooManyRequests);
    });

app.MapGet("/", () => "Hello World!");
// app.MapGet("/todos/{id:int}", (int id) => db.Todos.Find(id));
app.MapGet("/todos/{id:int}", (int id) => id + " response");
// app.MapGet("/todos/{text}", (string text) => db.Todos.Where(t => t.Text.Contains(text));
app.MapGet("/todos/{text}", (string text) => text + " text");
app.MapGet("/posts/{slug:regex(^[a-z0-9_-]+$)}", (string slug) => $"Post {slug}");

app.MapGet("/{id}", (HttpRequest request) =>
{
    var id = request.RouteValues["id"];
    var page = request.Query["page"];
    var customHeader = request.Headers["X-CUSTOM-HEADER"];

    // ...
});

app.MapPost("/", async (HttpRequest request) =>
{
    // var person = await request.ReadFromJsonAsync<Person>();

    // ...
});

// page is param in url like afce.org/deets/id?page=3
app.MapGet("/{id}", (int id,
                     int page,
                     [FromHeader(Name = "X-CUSTOM-HEADER")] string customHeader,
                     Service service) => { });

app.MapGet("/{id}", ([FromRoute] int id,
                     [FromQuery(Name = "p")] int page,
                     [FromServices] Service service,
                     [FromHeader(Name = "Content-Type")] string contentType) 
                     => {});

app.MapGet("/products", (int pageNumber) => $"Requesting page {pageNumber}");

app.MapPost("/", (Person person) => { });

app.MapGet("/{id}", ([FromRoute] int id,
                     [FromQuery(Name = "p")] int page,
                     [FromServices] Service service,
                     [FromHeader(Name = "Content-Type")] string contentType) 
                     => {});

app.MapGet("/products", (int? pageNumber) => $"Requesting page {pageNumber ?? 1}");

string ListProducts(int pageNumber = 1) => $"Requesting page {pageNumber}";

app.MapGet("/products2", ListProducts);

// app.MapPost("/products", (Product? product) => { });

app.MapGet("/", (HttpRequest request, HttpResponse response) =>
    response.WriteAsync($"Hello World {request.Query["name"]}"));

app.MapGet("/", async (CancellationToken cancellationToken) => 
    await Some.MakeLongRunningRequestAsync(cancellationToken));

/*
app.MapGet("/",   (               IDateTime dateTime) => dateTime.Now);
app.MapGet("/fs", ([FromServices] IDateTime dateTime) => dateTime.Now);
*/

// app.MapGet("/", (ClaimsPrincipal user) => user.Name);

app.MapPost(
    "/upload",
    async (IFormFile file) =>
    {
        var tempFile = Path.GetTempFileName();
        app.Logger.LogInformation(tempFile);
        using var stream = File.OpenWrite(tempFile);
        await file.CopyToAsync(stream);
    });

app.MapPost(
    "/upload_many",
    async (IFormFileCollection myFiles) =>
    {
        foreach (var file in myFiles)
        {
            var tempFile = Path.GetTempFileName();
            app.Logger.LogInformation(tempFile);
            using var stream = File.OpenWrite(tempFile);
            await file.CopyToAsync(stream);
        }
    });

// Setup the file server to serve static files.
app.UseFileServer();

app.MapGet("/", () =>
{
    throw new InvalidOperationException("Oops, the '/' route has thrown an exception.");
});

// GET /map?Point=12.3,10.1
app.MapGet("/map", (Point point) => $"Point: {point.X}, {point.Y}");

// GET /products?SortBy=xyz&SortDir=Desc&Page=99
app.MapGet("/products", (PagingData pageData) => $"SortBy:{pageData.SortBy}, " +
       $"SortDirection:{pageData.SortDirection}, CurrentPage:{pageData.CurrentPage}");

app.MapPost("/products", (Product product) => product);

app.MapPost("/uploadstream", async (IConfiguration config, HttpRequest request) =>
{
    var filePath = Path.Combine(config["StoredFilesPath"], Path.GetRandomFileName());

    await using var writeStream = File.Create(filePath);
    await request.BodyReader.CopyToAsync(writeStream);
});

app.MapGet("/hello", () => new { Message = "Hello World" });

app.MapGet("/hello", () => Results.Ok(new { Message = "Hello World" }));

/*
app.MapGet("/api/todoitems/{id}", async (int id, TodoDb db) =>
         await db.Todos.FindAsync(id) 
         is Todo todo
         ? Results.Ok(todo) 
         : Results.NotFound())
   .Produces<Todo>(StatusCodes.Status200OK)
   .Produces(StatusCodes.Status404NotFound);*/

app.MapGet("/hello", () => Results.Json(new { Message = "Hello World" }));

app.MapGet("/405", () => Results.StatusCode(405));

app.MapGet("/text", () => Results.Text("This is some text"));

var proxyClient = new HttpClient();
app.MapGet("/olikd", async () => 
{
    var stream = await proxyClient.GetStreamAsync("http://consoto/pokedex.json");
    // Proxy the response as JSON
    return Results.Stream(stream, "application/json");
});

app.MapGet("/old-path", () => Results.Redirect("/new-path"));

app.MapGet("/download", () => Results.File("myfile.text"));

/*
app.MapGet("/html", () => Results.Extensions.Html(@$"<!doctype html>
<html>
    <head><title>miniHTML</title></head>
    <body>
        <h1>Hello World</h1>
        <p>The time on the server is {DateTime.Now:O}</p>
    </body>
</html>"));*/

app.MapGet("/auth", [Authorize] () => "This endpoint requires authorization.");
app.MapGet("/", () => "This endpoint doesn't require authorization.");
app.MapGet("/Identity/Account/Login", () => "Sign in page at this endpoint.");

app.MapGet("/auth1", () => "This endpoint requires authorization")
   .RequireAuthorization();

app.MapGet("/admin", [Authorize("AdminsOnly")] () => 
                             "The /admin endpoint is for admins only.");

app.MapGet("/admin2", () => "The /admin2 endpoint is for admins only.")
   .RequireAuthorization("AdminsOnly");

app.MapGet("/", () => "This endpoint doesn't require authorization.");
app.MapGet("/Identity/Account/Login", () => "Sign in page at this endpoint.");

app.MapGet("/login", [AllowAnonymous] () => "This endpoint is for all roles.");

app.MapGet("/login2", () => "This endpoint also for all roles.")
   .AllowAnonymous();

app.UseCors();

app.MapGet("/cors", [EnableCors(MyAllowSpecificOrigins)] () => 
                           "This endpoint allows cross origin requests!");
app.MapGet("/cors2", () => "This endpoint allows cross origin requests!")
                     .RequireCors(MyAllowSpecificOrigins);

app.Run();

class Service { }
class ClaimsPrincipal {
    public string Name;
 }
record Person(string Name, int Age);

public class Some{
public async static Task MakeLongRunningRequestAsync(CancellationToken cancellationToken)
{

}
}

class Product
{
    // These are public fields, not properties.
    public int Id;
    public string? Name;
}