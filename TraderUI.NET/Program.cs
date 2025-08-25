using Microsoft.EntityFrameworkCore;
using Shared;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages();

//var options = new DbContextOptionsBuilder<SharedDbContext>()
//    .UseSqlServer("Server=localhost;Database=Tradexus;Trusted_Connection=True;Encrypt=False;")
//    .Options;

builder.Services.AddDbContext<SharedDbContext>(options =>
    options.UseSqlServer("Server=localhost;Database=Tradexus;Trusted_Connection=True;Encrypt=False;"));




var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();

app.MapRazorPages();

app.Run();
