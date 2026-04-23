namespace TestBuilder.Services
{
    /// <summary>
    /// Элемент списка профилей в левой панели.
    /// Содержит путь к файлу и красивое имя из поля "name" внутри JSON.
    /// </summary>
    public class GraphProfile
    {
        public string FilePath { get; }
        public string Name { get; }

        public GraphProfile(string filePath, string name)
        {
            FilePath = filePath;
            Name = name;
        }

        // Отображается в ListBox напрямую
        public override string ToString() => Name;
    }
}