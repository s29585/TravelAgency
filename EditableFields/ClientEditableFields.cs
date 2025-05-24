namespace TravelAgency.EditableFields;

public class ClientEditableFields
{
    public string FirstName { get; set; } = null!;
    public string LastName { get; set; } = null!;
    public string Email { get; set; } = null!;
    public string? Telephone { get; set; }
    public string? Pesel { get; set; }
}
