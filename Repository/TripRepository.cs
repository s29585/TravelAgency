using Microsoft.Data.SqlClient;
using TravelAgency.Model;

namespace TravelAgency.Repository;

public class TripsRepository
{
    private readonly string _connectionString;

    public TripsRepository(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<List<object>> GetAllTripsAsync()
    {
        var trips = new List<object>();
        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

    /*
    Pobiera listę wszystkich wycieczek wraz z powiązanymi krajami.

    Tabele:
    - Trip (t): zawiera podstawowe informacje o wycieczkach (IdTrip, Name, Description, DateFrom, DateTo, MaxPeople)
    - Country_Trip (ct): tabela łącznikowa między Trip a Country (relacja wiele-do-wielu)
    - Country (c): zawiera dane krajów (IdCountry, Name)

    LEFT JOIN-y zapewniają, że każda wycieczka zostanie zwrócona, nawet jeśli nie ma przypisanego kraju.

    Wynik jest posortowany rosnąco po ID wycieczki, co ułatwia grupowanie rekordów w C# (wielokrotne kraje dla jednej wycieczki).

    */
        var sql = @"
            SELECT
                t.IdTrip, t.Name, t.Description, t.DateFrom, t.DateTo, t.MaxPeople,
                c.IdCountry, c.Name AS CountryName
            FROM Trip t
            LEFT JOIN Country_Trip ct ON t.IdTrip = ct.IdTrip
            LEFT JOIN Country c ON ct.IdCountry = c.IdCountry
            ORDER BY t.IdTrip
        ";

        using var cmd = new SqlCommand(sql, connection);
        using var reader = await cmd.ExecuteReaderAsync();

        int? currentTripId = null;
        object currentTrip = null;
        List<object> countries = null;

        while (await reader.ReadAsync())
        {
            var tripId = reader.GetInt32(0);

            if (currentTripId != tripId)
            {
                if (currentTrip != null)
                {
                    trips.Add(currentTrip);
                }

                countries = new List<object>();
                currentTripId = tripId;
                currentTrip = new Trip()
                {
                    IdTrip = tripId,
                    Name = reader.GetString(1),
                    Description = reader.IsDBNull(2) ? null : reader.GetString(2),
                    DateFrom = reader.GetDateTime(3),
                    DateTo = reader.GetDateTime(4),
                    MaxPeople = reader.GetInt32(5),
                    Countries = new List<Country>()
                };
            }

            if (!reader.IsDBNull(6))
            {
                countries.Add(new Country()
                {
                    IdCountry = reader.GetInt32(6),
                    Name = reader.GetString(7)
                });
            }
        }

        if (currentTrip != null)
        {
            trips.Add(currentTrip);
        }

        return trips;
    }
}