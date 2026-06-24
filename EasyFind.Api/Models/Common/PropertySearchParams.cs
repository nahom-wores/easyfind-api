
namespace EasyFind.Api.Models.Dto.Common
{
    /// <summary>
    /// Request parameters for searching and filtering properties
    /// Combines pagination with filtering capabilities
    /// </summary>
    public class PropertyFilterParams
    {
        // Pagination
        private const int MaxPageSize = 50;
        private int _pageSize = 10;
        public int PageNumber { get; set; } = 1;
        public int PageSize
        {
            get => _pageSize;
            set => _pageSize = value > MaxPageSize ? MaxPageSize : value;
        }

        // Filtering
        
        
        
        // ==================== SORTING ====================

        public SortOption SortBy { get; set; } = SortOption.Latest; // Default to latest
        
        /// <summary>
        /// Sort direction
        /// "asc" = ascending (low to high, old to new)
        /// "desc" = descending (high to low, new to old)
        /// Default: "desc" (newest/highest first)
        /// </summary>
        public string SortOrder { get; set; } = "desc";
    }
    public enum SortOption
    {
        Latest = 0,      
        Popular = 1,    
        Nearby = 2  
    }
}
