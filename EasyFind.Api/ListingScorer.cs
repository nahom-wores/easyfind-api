using EasyFind.Api.Models.Enum;

namespace EasyFind.Api;

public static class ListingScorer
{
    public const int CountryWeight = 50;
    public const int CategoryWeight = 30;
    public const int DegreeWeight = 20;
    public const int FeaturedWeight = 10;

    // Pure function: given a listing's relevant fields + the user's preferences, compute the score.
    public static int Score(
        ListingType type,
        string countryCode,
        int? jobCategory,
        int? scholarshipField,
        int? degreeLevel,
        bool isFeatured,
        List<string> targetCountries,
        List<int> jobCats,
        List<int> schFields,
        int? targetDegree)
    {
        int score = 0;

        if (targetCountries.Contains(countryCode))
            score += CountryWeight;

        if (type == ListingType.Job && jobCategory != null && jobCats.Contains(jobCategory.Value))
            score += CategoryWeight;

        if (type == ListingType.Scholarship && scholarshipField != null && schFields.Contains(scholarshipField.Value))
            score += CategoryWeight;

        if (type == ListingType.Scholarship && targetDegree != null && degreeLevel != null && degreeLevel == targetDegree)
            score += DegreeWeight;

        if (isFeatured)
            score += FeaturedWeight;

        return score;
    }
}