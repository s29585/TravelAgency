using TravelAgency.EditableFields;
using TravelAgency.Repository;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();


var tripsRepo = new TripsRepository(connectionString);
var clientsRepo = new ClientRepository(connectionString);


// Pobiera wszystkie dostępne wycieczki wraz z ich podstawowymi informacjami.
app.MapGet("/api/trips", async () =>
    {
        var trips = await tripsRepo.GetAllTripsAsync();
        return Results.Ok(trips);
    })
    .WithName("GetAllTrips")
    .WithOpenApi(op => new(op)
    {
        Summary = "Pobierz wszystkie dostępne wycieczki",
        Description = "Zwraca listę wszystkich wycieczek dostępnych w systemie."
    })
    .Produces(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status404NotFound);


// Pobiera wszystkie wycieczki powiązane z konkretnym klientem.
app.MapGet("/api/clients/{id}/trips", async (int id) =>
    {
        var clientExists = await clientsRepo.ClientExistsAsync(id);
        if (!clientExists)
            return Results.NotFound($"Client with ID {id} does not exist.");

        var trips = await clientsRepo.GetTripsByClientIdAsync(id);

        if (trips.Count == 0)
            return Results.Ok(new { Message = "Client has no trips registered." });

        return Results.Ok(trips);
    })
    .WithOpenApi(op => new(op)
    {
        Summary = "Pobierz wycieczki klienta",
        Description = "Zwraca wszystkie wycieczki przypisane do danego klienta na podstawie jego ID."
    })
    .Produces(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status404NotFound);


// Tworzy nowy rekord klienta.
app.MapPost("/api/clients", async (ClientEditableFields input) =>
    {
        if (string.IsNullOrWhiteSpace(input.FirstName) ||
            string.IsNullOrWhiteSpace(input.LastName) ||
            string.IsNullOrWhiteSpace(input.Email))
        {
            return Results.BadRequest("FirstName, LastName and Email are required.");
        }

        if (!input.Email.Contains("@"))
        {
            return Results.BadRequest("Email format is invalid.");
        }

        try
        {
            var newId = await clientsRepo.CreateClientAsync(input);
            return Results.Created($"/api/clients/{newId}", new { IdClient = newId });
        }
        catch (Exception ex)
        {
            return Results.Problem("Failed to create client: " + ex.Message);
        }
    })
    .WithOpenApi(op => new(op)
    {
        Summary = "Utwórz nowego klienta",
        Description = "Dodaje nowego klienta do bazy danych. Wymagane pola: FirstName, LastName, Email."
    })
    .Produces(StatusCodes.Status201Created)
    .Produces(StatusCodes.Status400BadRequest)
    .Produces(StatusCodes.Status500InternalServerError);


// Rejestruje klienta na konkretną wycieczkę.
app.MapPut("/api/clients/{id}/trips/{tripId}", async (int id, int tripId) =>
    {
        var error = await clientsRepo.RegisterClientToTripAsync(id, tripId);
        if (error != null)
        {
            if (error.Contains("not found"))
            {
                return Results.NotFound(error);
            }
            else if (error.Contains("already registered"))
            {
                return Results.Conflict(error);
            }
            else
            {
                return Results.BadRequest(error);
            }
        }

        return Results.Ok("Client registered for the trip successfully.");
    })
    .WithOpenApi(op => new(op)
    {
        Summary = "Zarejestruj klienta na wycieczkę",
        Description = "Rejestruje istniejącego klienta na wybraną wycieczkę."
    })
    .Produces(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status400BadRequest)
    .Produces(StatusCodes.Status404NotFound)
    .Produces(StatusCodes.Status409Conflict);


// Usuwa rejestrację klienta z wycieczki.
app.MapDelete("/api/clients/{id:int}/trips/{tripId:int}", async (int id, int tripId) =>
    {
        var error = await clientsRepo.DeleteClientRegistrationAsync(id, tripId);
        if (error != null)
            return Results.NotFound(error);

        return Results.Ok("Registration deleted successfully.");
    })
    .WithOpenApi(op => new(op)
    {
        Summary = "Usuń rejestrację klienta z wycieczki",
        Description = "Usuwa powiązanie klienta z daną wycieczką."
    })
    .Produces(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status404NotFound);


app.Run();