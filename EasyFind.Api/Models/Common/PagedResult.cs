namespace EasyFind.Api.Models.Dto.Common
{
    public class PagedResult<T>
    {
        // The actual data for current page
        public List<T> Data { get; set; }

        // Current page number (1-based)
        public int PageNumber { get; set; }

        // Number of items per page
        public int PageSize { get; set; }

        // Total number of items across all pages
        public int TotalRecords { get; set; }

        // Calculated: How many pages total
        public int TotalPages => (int)Math.Ceiling(TotalRecords / (double)PageSize);

        // Helper for frontend: Can user go back?
        public bool HasPreviousPage => PageNumber > 1;

        // Helper for frontend: Can user go forward?
        public bool HasNextPage => PageNumber < TotalPages;
    }

   
}
