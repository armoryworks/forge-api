namespace Forge.Platform.Enums;

public enum IdentityDocumentType
{
    // Generic list identifiers (used when uploading without specifying exact document)
    ListA,
    ListB,
    ListC,

    // Specific types (legacy / future granularity)
    Passport,
    PermanentResidentCard,
    EmploymentAuthorizationDoc,
    ForeignPassportI551,
    DriversLicense,
    StateIdCard,
    SchoolId,
    VoterRegistrationCard,
    MilitaryId,
    SsnCard,
    BirthCertificate,
    CitizenshipCertificate,
    Other,
}
