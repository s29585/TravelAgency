using Microsoft.Data.SqlClient;
using TravelAgency.EditableFields;
using TravelAgency.Model;

namespace TravelAgency.Repository
{
    public class ClientRepository
    {
        private readonly string _connectionString;

        public ClientRepository(string connectionString)
        {
            _connectionString = connectionString;
        }

        public async Task<bool> ClientExistsAsync(int clientId)
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            /*
            Zapytanie SQL sprawdzające istnienie klienta o podanym identyfikatorze (IdClient).
            Zwraca liczbę rekordów spełniających warunek (0 jeśli nie istnieje, 1 jeśli istnieje).
            Użycie zapytań sparametryzowanych zabezpiecza przed atakami typu SQL Injection.
            */
            using var cmd = new SqlCommand("SELECT COUNT(*) FROM Client WHERE IdClient = @clientId", connection);
            cmd.Parameters.AddWithValue("@clientId", clientId);

            var count = (int)await cmd.ExecuteScalarAsync();
            return count > 0;
        }

        public async Task<List<ClientTrip>> GetTripsByClientIdAsync(int clientId)
        {
            var trips = new List<ClientTrip>();

            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            /*
            Pobiera wszystkie wycieczki, na które klient jest zarejestrowany.
            Dołączone są daty rejestracji i płatności klienta.
            Dodatkowo zwraca powiązane kraje dla każdej wycieczki, jeśli istnieją.
            Wyniki są posortowane po identyfikatorze wycieczki (IdTrip).
            */
            var sql = @"
                SELECT 
                    t.IdTrip, t.Name, t.Description, t.DateFrom, t.DateTo, t.MaxPeople,
                    ct.RegisteredAt, ct.PaymentDate,
                    c.IdCountry, c.Name AS CountryName
                FROM Client_Trip ct
                INNER JOIN Trip t ON ct.IdTrip = t.IdTrip
                LEFT JOIN Country_Trip ctrip ON t.IdTrip = ctrip.IdTrip
                LEFT JOIN Country c ON ctrip.IdCountry = c.IdCountry
                WHERE ct.IdClient = @clientId
                ORDER BY t.IdTrip
            ";

            using var cmd = new SqlCommand(sql, connection);
            cmd.Parameters.AddWithValue("@clientId", clientId);

            using var reader = await cmd.ExecuteReaderAsync();

            int? currentTripId = null;
            ClientTrip currentTrip = null;
            List<Country> countries = null;

            while (await reader.ReadAsync())
            {
                var tripId = reader.GetInt32(0);

                if (currentTripId != tripId)
                {
                    if (currentTrip != null)
                    {
                        trips.Add(currentTrip);
                    }

                    countries = new List<Country>();
                    currentTripId = tripId;

                    DateTime? registeredAt = null;
                    DateTime? paymentDate = null;

                    if (!reader.IsDBNull(6))
                        registeredAt = ParseIntToDate(reader.GetInt32(6));

                    if (!reader.IsDBNull(7))
                        paymentDate = ParseIntToDate(reader.GetInt32(7));

                    currentTrip = new ClientTrip
                    {
                        IdTrip = tripId,
                        Name = reader.GetString(1),
                        Description = reader.IsDBNull(2) ? null : reader.GetString(2),
                        DateFrom = reader.GetDateTime(3),
                        DateTo = reader.GetDateTime(4),
                        MaxPeople = reader.GetInt32(5),
                        RegisteredAt = registeredAt,
                        PaymentDate = paymentDate,
                        Countries = countries
                    };
                }

                if (!reader.IsDBNull(8))
                {
                    countries.Add(new Country
                    {
                        IdCountry = reader.GetInt32(8),
                        Name = reader.GetString(9)
                    });
                }
            }

            if (currentTrip != null)
            {
                trips.Add(currentTrip);
            }

            return trips;
        }

        public async Task<int> CreateClientAsync(ClientEditableFields input)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));

            var firstName = input.FirstName ?? throw new ArgumentNullException(nameof(input.FirstName));
            var lastName = input.LastName ?? throw new ArgumentNullException(nameof(input.LastName));
            var email = input.Email ?? throw new ArgumentNullException(nameof(input.Email));
            var telephone = (object?)input.Telephone ?? DBNull.Value;
            var pesel = (object?)input.Pesel ?? DBNull.Value;

            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();
            
            /*
            Dodaje nowego klienta do tabeli Client z podanymi danymi.
            Po wstawieniu zwraca nowo wygenerowany identyfikator klienta (IdClient)
            korzystając z funkcji scope_identity(), która zwraca ostatni identyfikator w bieżącym zakresie.
            */
            var sql = @"
                INSERT INTO Client (FirstName, LastName, Email, Telephone, Pesel)
                VALUES (@FirstName, @LastName, @Email, @Telephone, @Pesel);
                SELECT CAST(scope_identity() AS int);
            ";

            using var cmd = new SqlCommand(sql, connection);

            cmd.Parameters.AddWithValue("@FirstName", firstName);
            cmd.Parameters.AddWithValue("@LastName", lastName);
            cmd.Parameters.AddWithValue("@Email", email);
            cmd.Parameters.AddWithValue("@Telephone", telephone);
            cmd.Parameters.AddWithValue("@Pesel", pesel);

            try
            {
                return (int)await cmd.ExecuteScalarAsync();
            }
            catch (Exception ex)
            {
                throw new Exception("Błąd podczas dodawania klienta: " + ex.Message, ex);
            }
        }

        public async Task<string> RegisterClientToTripAsync(int clientId, int tripId)
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            var checkClientCmd = new SqlCommand("SELECT COUNT(*) FROM Client WHERE IdClient = @id", connection);
            checkClientCmd.Parameters.AddWithValue("@id", clientId);
            var clientExists = (int)await checkClientCmd.ExecuteScalarAsync() > 0;
            if (!clientExists)
                return $"Client with ID {clientId} not found.";

            var checkTripCmd = new SqlCommand("SELECT MaxPeople FROM Trip WHERE IdTrip = @tripId", connection);
            checkTripCmd.Parameters.AddWithValue("@tripId", tripId);
            var maxPeopleObj = await checkTripCmd.ExecuteScalarAsync();
            if (maxPeopleObj == null)
                return $"Trip with ID {tripId} not found.";
            int maxPeople = (int)maxPeopleObj;

            var checkRegistrationCmd =
                new SqlCommand("SELECT COUNT(*) FROM Client_Trip WHERE IdClient = @id AND IdTrip = @tripId",
                    connection);
            checkRegistrationCmd.Parameters.AddWithValue("@id", clientId);
            checkRegistrationCmd.Parameters.AddWithValue("@tripId", tripId);
            var alreadyRegistered = (int)await checkRegistrationCmd.ExecuteScalarAsync() > 0;
            if (alreadyRegistered)
                return "Client is already registered for this trip.";

            var countParticipantsCmd =
                new SqlCommand("SELECT COUNT(*) FROM Client_Trip WHERE IdTrip = @tripId", connection);
            countParticipantsCmd.Parameters.AddWithValue("@tripId", tripId);
            int currentCount = (int)await countParticipantsCmd.ExecuteScalarAsync();

            if (currentCount >= maxPeople)
                return "Maximum number of participants reached for this trip.";

            var todayInt = int.Parse(DateTime.UtcNow.ToString("yyyyMMdd"));
            
            /*
            Wstawia nową rejestrację klienta na wycieczkę do tabeli Client_Trip.
            Parametry @id i @tripId określają klienta i wycieczkę.
            Pole RegisteredAt oznacza datę rejestracji w formacie liczbowym (np. YYYYMMDD).
            Pole PaymentDate pozostaje puste (NULL), ponieważ płatność nie została jeszcze zrealizowana.
            */
            var insertCmd = new SqlCommand(@"
                INSERT INTO Client_Trip (IdClient, IdTrip, RegisteredAt, PaymentDate)
                VALUES (@id, @tripId, @registeredAt, NULL)
             ", connection);
            
            insertCmd.Parameters.AddWithValue("@id", clientId);
            insertCmd.Parameters.AddWithValue("@tripId", tripId);
            insertCmd.Parameters.AddWithValue("@registeredAt", todayInt);

            await insertCmd.ExecuteNonQueryAsync();

            return null;
        }

        public async Task<string?> DeleteClientRegistrationAsync(int clientId, int tripId)
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            var checkRegistrationCmd =
                new SqlCommand("SELECT COUNT(*) FROM Client_Trip WHERE IdClient = @id AND IdTrip = @tripId",
                    connection);
            checkRegistrationCmd.Parameters.AddWithValue("@id", clientId);
            checkRegistrationCmd.Parameters.AddWithValue("@tripId", tripId);
            var exists = (int)await checkRegistrationCmd.ExecuteScalarAsync() > 0;

            if (!exists)
                return "Registration not found.";

            /*
            Usuwa rejestrację klienta z wycieczki w tabeli Client_Trip.
            Warunek WHERE ogranicza usunięcie do rekordu odpowiadającego danemu klientowi (@id) oraz wycieczce (@tripId).
            */
            var deleteCmd = new SqlCommand("DELETE FROM Client_Trip WHERE IdClient = @id AND IdTrip = @tripId",
                connection);
            
            deleteCmd.Parameters.AddWithValue("@id", clientId);
            deleteCmd.Parameters.AddWithValue("@tripId", tripId);

            await deleteCmd.ExecuteNonQueryAsync();

            return null;
        }

        private DateTime ParseIntToDate(int yyyymmdd)
        {
            var s = yyyymmdd.ToString();
            int year = int.Parse(s.Substring(0, 4));
            int month = int.Parse(s.Substring(4, 2));
            int day = int.Parse(s.Substring(6, 2));
            return new DateTime(year, month, day);
        }
    }
}