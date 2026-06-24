namespace EasyFind.Api.Models.Dto.Common
{
    /// <summary>
    /// Request parameters for pagination
    /// Client sends this to specify which page they want
    /// </summary>
    public class PaginationParams
    {
        private const int MaxPageSize = 50; // Safety limit
        private int _pageSize = 10; // Default

        public int PageNumber { get; set; } = 1;

        public int PageSize
        {
            get => _pageSize; // When someone READS PageSize, return the private field

            set => _pageSize = value > MaxPageSize ? MaxPageSize : value;
            // When someone WRITES to PageSize:
            // - If they send 100, set it to 50 (MaxPageSize)
            // - If they send 20, set it to 20 (valid)
            // This is a TERNARY OPERATOR: condition ? ifTrue : ifFalse
        }
    }
}
