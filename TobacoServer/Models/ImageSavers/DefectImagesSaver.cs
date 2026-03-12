using OpenCvSharp;

namespace TobacoServer.Models.ImageSavers
{
    abstract public class DefectImagesSaver
    {
        protected static object _ensureDirectoryLocker = new object();
        protected static object _locker = new object();


        public static string Save(string zoneName, Mat mat, string defectName)
        {
            lock (_locker)
            {
                try
                {
                    var path = Path.Join(Directory.GetCurrentDirectory(), "Images", DateOnly.FromDateTime(DateTime.Now).ToString());
                    var root = EnsureTodaysDirectoryCreated(path);
                    path = Path.Join(root.FullName, defectName);
                    var defectDirectory = EnsureTodaysDirectoryCreated(path);
                    var filepath = $"{defectDirectory.FullName}/{zoneName}_{DateTime.Now.ToString("HH_mm_ss")}.jpeg";
                    var result = Cv2.ImWrite(filepath, mat);
                    return filepath;
                }
                catch (Exception ex)
                {
                    throw;
                }
            }
        }


        public static Mat CreateImageGrid(List<Mat> images, int cols = -1)
        {
            if (images == null || images.Count == 0)
                return new Mat();

            // количество колонок по умолчанию = sqrt(N)
            if (cols == -1)
                cols = (int)Math.Ceiling(Math.Sqrt(images.Count));

            int rows = (int)Math.Ceiling((double)images.Count / cols);

            // 🔹 размер ячейки (разбиваем FullHD на сетку)
            int cellWidth = 1920 / cols;
            int cellHeight = 1080 / rows;
            var cellSize = new OpenCvSharp.Size(cellWidth, cellHeight);

            // 🔹 приводим все изображения к размеру ячеек
            List<Mat> resizedImages = new List<Mat>();
            foreach (var img in images)
            {
                if (img.Empty())
                    continue;

                Mat resized = new Mat();
                Cv2.Resize(img, resized, cellSize);
                resizedImages.Add(resized);
            }

            // 🔹 собираем построчно
            List<Mat> rowImages = new List<Mat>();
            for (int i = 0; i < rows; i++)
            {
                List<Mat> currentRow = new List<Mat>();
                for (int j = 0; j < cols; j++)
                {
                    int index = i * cols + j;
                    if (index < resizedImages.Count)
                        currentRow.Add(resizedImages[index]);
                    else
                        currentRow.Add(new Mat(cellSize, resizedImages[0].Type(), Scalar.Black)); // заполняем пустыми
                }

                Mat rowImage = new Mat();
                Cv2.HConcat(currentRow, rowImage);
                rowImages.Add(rowImage);
            }

            // 🔹 объединяем строки
            Mat gridImage = new Mat();
            Cv2.VConcat(rowImages, gridImage);

            // 🔹 финальная подгонка под FullHD
            Mat finalImage = new Mat();
            Cv2.Resize(gridImage, finalImage, new OpenCvSharp.Size(1920, 1080));

            return finalImage;
        }


        protected static DirectoryInfo EnsureTodaysDirectoryCreated(string path)
        {
            lock (_ensureDirectoryLocker)
            {
                return Directory.CreateDirectory(path);
            }
        }
    }
}
