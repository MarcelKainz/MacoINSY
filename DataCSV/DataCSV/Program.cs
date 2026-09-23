using System;
using System.IO;
using System.Text;

class Program
{
    static readonly Random random = new Random();
    static readonly Random rand = new Random();

    static string GenerateRandomString(int length)
    {
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";

        char[] result = new char[length];

        for (int i = 0; i < length; i++)
        {
            result[i] = chars[random.Next(chars.Length)];
        }

        return new string(result);
    }

    static void Main()
    {
        const int numberOfRows = 1_000_000;
        const int stringLength = 10;

        string fileName = "daten.csv";

        using (StreamWriter writer = new StreamWriter(
                   fileName,
                   false,
                   new UTF8Encoding(false),
                   65536))
        {
            // Kopfzeile
            writer.WriteLine("LineNumber,Value1,Value2,RandInt1,RandInt2,noten1,noten2");

            // 1.000.000 Datensätze erzeugen
            for (int i = 1; i <= numberOfRows; i++)
            {
                string value = GenerateRandomString(stringLength);

                // Zufallszahlen von 0 bis einschließlich 2.000.000.000
                int value2 = rand.Next(0, int.MaxValue);

                int noten = rand.Next(1, 6);

                writer.WriteLine($"{i},{value},{value},{value2},{value2},{noten},{noten}");
            }
        }

        Console.WriteLine();
        Console.WriteLine($"Fertig! Datei wurde erstellt: {Path.GetFullPath(fileName)}");
    }
}

//Test Commit