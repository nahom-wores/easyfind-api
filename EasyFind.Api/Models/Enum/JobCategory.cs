namespace EasyFind.Api.Models.Enum;

public enum JobCategory
{
    // Tech & Engineering
    InformationTechnology = 0,
    Engineering = 1,

    // Health
    Healthcare = 2,
    Nursing = 3,

    // Education & Research
    Education = 4,
    ResearchAcademia = 5,

    // Business & Finance
    BusinessFinance = 6,
    SalesMarketing = 7,
    Administration = 8,

    // Trades & Services
    Hospitality = 9,
    Construction = 10,
    Logistics = 11,
    Agriculture = 12,

    // Social & Creative
    SocialDevelopment = 13,
    CreativeMedia = 14,
    LegalPolicy = 15,

    // Catch-all
    Other = 99
}
public enum ScholarshipField
{
    Engineering = 0,
    ComputerScience = 1,
    Medicine = 2,
    NaturalSciences = 3,
    SocialSciences = 4,
    Business = 5,
    Law = 6,
    Humanities = 7,
    Agriculture = 8,
    Education = 9,
    ArtsDesign = 10,
    AnyField = 99
}

public enum DegreeLevel
{
    Undergraduate = 0,
    Masters = 1,
    Phd = 2,
    PostDoc = 3,
    ShortCourse = 4
}

public enum FundingType
{
    FullyFunded = 0,
    PartialFunding = 1,
    TuitionOnly = 2,
    Stipend = 3
}
public enum ListingType
{
    Job = 0,
    Scholarship = 1
}
public enum EmploymentType
{
    FullTime = 0,
    PartTime = 1,
    Contract = 2,
    Temporary = 3,
    Internship = 4
}
public enum SalaryPeriod
{
    Yearly = 0,
    Monthly = 1,
    Hourly = 2
}

public enum Currency
{
    USD = 0,
    EUR = 1,
    GBP = 2,
    ETB = 3,
    AED = 4,
    QAR = 5,
    SAR = 6,
    CAD = 7
}