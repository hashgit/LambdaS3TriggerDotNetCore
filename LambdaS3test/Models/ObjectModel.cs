namespace LambdaS3test.Models
{
    public class ObjectModel
    {
        public string ImageData { get; set; }
        public ObjectState State { get; set; }
        public OcrResult OcrResult { get; set; }
    }

    public enum ObjectState
    {
        UNKNOWN = 0,
        PROCESSING = 1,
        SUCCESS = 2,
        FAILED = 3
    }
}