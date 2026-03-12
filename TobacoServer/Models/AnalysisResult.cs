namespace TobacoServer.Models.ResponseDTOs
{
    internal class AnalysisResult
    {
        public string Image { get; set; }
        public string Pose { get; set; }
        public List<string> Persons { get; set; }
    }
}
