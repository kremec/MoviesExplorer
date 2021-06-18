using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.Net;
using System.Web;
using Newtonsoft.Json;

namespace MoviesExplorer
{
    class Config
    {
        public string unsortedMoviesPath { get; set; }
        public string externalMoviesPath { get; set; }
        public string outputFilesPath { get; set; }
        public string moviesDestinationYear { get; set; }
        public string moviesDestinationGenre { get; set; }
        public string moviesDestinationActors { get; set; }
    }

    class Program
    {
        static void Main(string[] args)
        {
            Config config = JsonConvert.DeserializeObject<Config>(File.ReadAllText(@"C:\ut\bin\MoviesExplorer\config.json"));

            Console.WriteLine("Press:");
            Console.WriteLine("1 to update database and output commands for only new movies");
            Console.WriteLine("2 to output commands for all movies in existing database");
            var choice = Console.ReadLine();

            if (choice == "1")
            {
                UpdateDatabaseAndOutputNew();
            }
            else if (choice == "2")
            {
                OutputFromDatabase();
            }
        }

        static void UpdateDatabaseAndOutputNew()
        {
            Config config = JsonConvert.DeserializeObject<Config>(File.ReadAllText(@"C:\ut\bin\MoviesExplorer\config.json"));

            string[] directoryPaths = Directory.GetDirectories(config.unsortedMoviesPath);
            int allMovies = 0;

            List<Movie> newMovies = new List<Movie>();
            List<string> moviesNotFound = new List<string>();
            bool isMovieNotFound = false;

            try
            {
                foreach (string path in directoryPaths)
                {
                    #region GET DIRECTORIES FROM HIGHER DIRECTORY
                    DirectoryInfo directoryInfo = new DirectoryInfo(path);

                    string upDirectoryName = directoryInfo.Name;
                    #endregion

                    #region SCAN SUBDIRECTORIES OF HIGHER DIRECTORY & GET INFO TO DATABASE

                    string[] subDirectoryPaths = Directory.GetDirectories(path);
                    foreach (string subpath in subDirectoryPaths)
                    {
                        allMovies++;

                        // Get movie info (Form: movie_name.movie_year)
                        DirectoryInfo subDirectoryInfo = new DirectoryInfo(subpath);
                        string subDirectoryName = subDirectoryInfo.Name;

                        string[] splitName = subDirectoryName.Split('.');

                        // Get movie's year
                        string movieYear = splitName[splitName.Length - 1];

                        // Get and fix up movie's title
                        // (We have a system where if the movie starts with "The ..." or "A ...", we renamed the folder so this part is at the end, which helps with alphabetical sorting and easier searching of movies)
                        string movieTitle = "";
                        for (int i = 0; i < splitName.Length - 1; i++)
                        {
                            movieTitle += splitName[i];

                            if (splitName.Length > 2 && i < splitName.Length - 2)
                            {
                                movieTitle += ".";
                            }
                        }
                        if (movieTitle.Contains(", The"))
                        {
                            movieTitle = "The " + movieTitle.Substring(0, movieTitle.Length - 5);
                        }
                        else if (movieTitle.Contains(", A"))
                        {
                            movieTitle = "A " + movieTitle.Substring(0, movieTitle.Length - 3);
                        }

                        // Open the database and check if movie currently being checked is already in the said database
                        MongoCRUD db = new MongoCRUD("MovieDatabase");
                        var records = db.LoadRecords<Movie>("Movies");

                        bool movieAlreadyInDatabase = false;
                        foreach (var record in records)
                        {
                            if (record.folderName == subDirectoryName)
                            {
                                movieAlreadyInDatabase = true;
                            }
                        }

                        // If the movie is not in the database (was added after last update of the database)
                        if (movieAlreadyInDatabase == false)
                        {
                            // Send an API call with movie info (title and year) and save the results (which are in JSON form)
                            string requestAddress = string.Format("http://www.omdbapi.com/?apikey=327ed302") + "&t=" + movieTitle + "&y=" + movieYear;
                            WebRequest requestObject = WebRequest.Create(requestAddress);
                            requestObject.Method = "GET";

                            HttpWebResponse responseObject = null;
                            responseObject = (HttpWebResponse)requestObject.GetResponse();

                            string streamResult = null;
                            using (Stream stream = responseObject.GetResponseStream())
                            {
                                StreamReader sr = new StreamReader(stream);
                                streamResult = sr.ReadToEnd();
                                sr.Close();
                            }

                            // Deserialise returned JSON info from API call and get the data in a new Movie object (see class Movie for all variables, stored in it)
                            Movie movie = JsonConvert.DeserializeObject<Movie>(streamResult);

                            movie.OriginalMovieData = new OriginalMovieData
                            {
                                originalTitle = movie.title,
                                originalYear = movie.year,
                                originalGenre = movie.genre,
                                originalActors = movie.actors
                            };

                            movie.folderName = subDirectoryName;

                            // Add Movie object wih all the data to a temporary list of all the new movies
                            newMovies.Add(movie);

                            // Output a warning of possible error if the movie title from directory name isn't the same as title that was returned in the API call
                            // Errors can happen if there was just a typoo in folder name; or API couldn't find the searched movie and returned info from movie with the most similar name
                            if (movieTitle != movie.title)
                            {
                                movie.possibleError = true;
                                Console.WriteLine("Possible error: " + movie.title + " - (" + subDirectoryName + ")");

                                // If API can't find any movie with a similar name, it doesn't return anything; in which case the info about the movie is stored in temporary list to be displayed as warning
                                if (movie.title == null)
                                {
                                    moviesNotFound.Add(movie.folderName);
                                    isMovieNotFound = true;
                                }
                            }
                            else
                            {
                                movie.possibleError = false;
                            }

                            Console.WriteLine("");
                        }
                    }
                    #endregion
                }

                // Output warning for all movies that could be wronly processed (see comments above)
                // As long as there is an error with any movie, the program won't let you write in database to avoid confusion and wrong entries in database
                if (isMovieNotFound == true)
                {
                    foreach (string movieNotFound in moviesNotFound)
                    {
                        Console.WriteLine("Movie: " + movieNotFound + " not found!");
                    }
                    Console.WriteLine("Fix these names and try again!");
                    Console.ReadLine();
                    Environment.Exit(0);
                }

                // If there were no errors within previous processes, an entry with all the info from new movie gets added in database
                else
                {
                    foreach (Movie newMovie in newMovies)
                    {
                        Console.WriteLine("Adding movie: " + newMovie.title + " to database");

                        MongoCRUD db = new MongoCRUD("MovieDatabase");
                        db.InsertRecord("Movies", newMovie);
                    }
                }

                #region GENERATE COMMANDS FOR SHELL SCRIPT

                string pathSource = config.externalMoviesPath + "/";
                string moviesDestinationYear = config.moviesDestinationYear;
                string moviesDestinationGenre = config.moviesDestinationGenre;
                string moviesDestinationActors = config.moviesDestinationActors;

                int decades = 2030;
                List<string> possibleGenres = new List<string>() { "Action", "Adventure", "Animation", "Biography", "Comedy", "Crime", "Drama", "Documentary", "Family", "Fantasy", "History", "Horror", "Music", "Musical", "Mystery", "Romance", "Sci-Fi", "Short", "Sport", "Thriller", "War", "Western" };

                string outputFilesPath = config.outputFilesPath;
                StreamWriter sw = new StreamWriter(Path.Combine(outputFilesPath, "NewMovies.txt"));


                // Output movies by year, sorted into decades
                for (int decade = 1940; decade < decades;)
                {
                    foreach (var newMovie in newMovies)
                    {
                        if (decade <= newMovie.year && newMovie.year <= decade + 9)
                        {
                            string finalFolderName = newMovie.folderName.Replace(" ", "\\ ").Replace("'", "\\'").Replace("&", "\\&");
                            string output = "ln -s " + pathSource + SortTitleToFolder(newMovie.folderName) + "/" + finalFolderName + " " + moviesDestinationYear + decade.ToString().Remove(3, 1) + "x";
                            sw.WriteLine(output);
                            Console.WriteLine(output);
                        }
                    }
                    decade += 10;
                }

                // Output movies by genres, defined in list possibleGenres (see above)
                foreach (var newMovie in newMovies)
                {
                    if (newMovie.genre != null && newMovie.genre != "N/A")
                    {
                        string currentGenre = newMovie.genre;
                        string[] genres = currentGenre.Split(new string[] { ", " }, StringSplitOptions.None);
                        foreach (var genre in genres)
                        {
                            string finalFolderName = newMovie.folderName.Replace(" ", "\\ ").Replace("'", "\\'").Replace("&", "\\&");
                            string output = "ln -s " + pathSource + SortTitleToFolder(newMovie.folderName) + "/" + finalFolderName + " " + moviesDestinationGenre + genre;
                            sw.WriteLine(output);
                            Console.WriteLine(output);
                        }
                    }
                }

                // Output movies by actors, defined in an subsidiary config file
                string logPath = config.outputFilesPath + "SeznamIgralcev.txt";

                var logFile = File.ReadAllLines(logPath);
                var possibleActors = new List<string>(logFile);

                foreach (var newMovie in newMovies)
                {
                    if (newMovie.actors != null)
                    {
                        string currentActors = newMovie.actors;
                        string[] actors = currentActors.Split(new string[] { ", " }, StringSplitOptions.None);
                        foreach (var actor in actors)
                        {
                            if (possibleActors.Contains(actor))
                            {
                                string finalFolderName = newMovie.folderName.Replace(" ", "\\ ").Replace("'", "\\'").Replace("&", "\\&");
                                string output = "ln -s " + pathSource + SortTitleToFolder(newMovie.folderName) + "/" + finalFolderName + " " + moviesDestinationActors + actor;
                                sw.WriteLine(output);
                                Console.WriteLine(output);
                            }
                        }
                    }
                }

                sw.Close();
                #endregion
                Console.WriteLine(allMovies);
            }

            catch (Exception e)
            {
                Console.WriteLine("Error: " + e.Message);
                Console.ReadLine();
                Environment.Exit(0);
            }

            Console.ReadLine();
        }

        static void OutputFromDatabase()
        {
            // The program writes a shell script that makes folders for all categories and then writes commands that will make symbolic links of movies sorted out
            Config config = JsonConvert.DeserializeObject<Config>(File.ReadAllText(@"C:\ut\bin\MoviesExplorer\config.json"));

            MongoCRUD db = new MongoCRUD("MovieDatabase");
            var records = db.LoadRecords<Movie>("Movies");

            string docPath = config.outputFilesPath;
            StreamWriter sw = new StreamWriter(Path.Combine(docPath, "MoviesFromDatabase.sh"));

            sw.WriteLine("#!/bin/sh");

            sw.WriteLine("cd /mnt/SYS_DATA/metashare/xt/filmi-test/");

            sw.WriteLine("rm -r actor");
            sw.WriteLine("rm -r genre");
            sw.WriteLine("rm -r year");

            sw.WriteLine("mkdir actor");
            sw.WriteLine("mkdir genre");
            sw.WriteLine("mkdir year");


            #region OUTPUT BY YEAR
            int decades = 2030;
            string sourcePathYear = config.externalMoviesPath + "/";
            string destinationPathYear = config.moviesDestinationYear;

            sw.WriteLine("cd " + destinationPathYear);

            for (int decade = 1940; decade < decades;)
            {
                sw.WriteLine("mkdir " + decade.ToString().Remove(3, 1) + "x");
                Console.WriteLine("mkdir " + decade.ToString().Remove(3, 1) + "x");
                decade += 10;
            }

            for (int decade = 1940; decade < decades;)
            {
                foreach (var record in records)
                {
                    if (decade <= record.year && record.year <= decade + 9)
                    {
                        string finalFolderName = record.folderName.Replace(" ", "\\ ").Replace("'", "\\'").Replace("&", "\\&");
                        string output = "ln -s " + sourcePathYear + SortTitleToFolder(record.folderName) + "/" + finalFolderName + " " + destinationPathYear + decade.ToString().Remove(3, 1) + "x";
                        sw.WriteLine(output);
                        Console.WriteLine(output);
                    }
                }
                decade += 10;
            }
            #endregion

            #region OUTPUT BY GENRE
            List<string> possibleGenres = new List<string>() { "Action", "Adventure", "Animation", "Biography", "Comedy", "Crime", "Drama", "Documentary", "Family", "Fantasy", "History", "Horror", "Music", "Musical", "Mystery", "Romance", "Sci-Fi", "Short", "Sport", "Thriller", "War", "Western" };
            string sourcePathGenre = config.externalMoviesPath + "/";
            string destinationPathGenre = config.moviesDestinationGenre;

            sw.WriteLine("cd " + destinationPathGenre);

            foreach (var possibleGenre in possibleGenres)
            {
                sw.WriteLine("mkdir " + possibleGenre);
                Console.WriteLine("mkdir " + possibleGenre);
            }

            foreach (var record in records)
            {
                if (record.genre != null && record.genre != "N/A")
                {
                    string currentGenre = record.genre;
                    string[] genres = currentGenre.Split(new string[] { ", " }, StringSplitOptions.None);
                    foreach (var genre in genres)
                    {
                        string finalFolderName = record.folderName.Replace(" ", "\\ ").Replace("'", "\\'").Replace("&", "\\&");
                        string output = "ln -s " + sourcePathGenre + SortTitleToFolder(record.folderName) + "/" + finalFolderName + " " + destinationPathGenre + genre;
                        sw.WriteLine(output);
                        Console.WriteLine(output);
                    }
                }
            }
            #endregion

            #region OUTPUT BY ACTORS
            string sourcePathActors = config.externalMoviesPath + "/";
            string destinationPathActors = config.moviesDestinationActors;

            string LogPath = config.outputFilesPath + "SeznamIgralcev.txt";

            var logFile = File.ReadAllLines(LogPath);
            var possibleActors = new List<string>(logFile);

            sw.WriteLine("cd " + destinationPathActors);

            foreach (var possibleActor in possibleActors)
            {
                string finalActor = possibleActor.Replace(" ", "\\ ").Replace("'", "\\'").Replace("&", "\\&");
                sw.WriteLine("mkdir " + finalActor);
                Console.WriteLine("mkdir " + finalActor);
            }

            foreach (var record in records)
            {
                if (record.actors != null)
                {
                    string currentActors = record.actors;
                    string[] actors = currentActors.Split(new string[] { ", " }, StringSplitOptions.None);
                    foreach (var actor in actors)
                    {
                        if (possibleActors.Contains(actor))
                        {
                            string finalFolderName = record.folderName.Replace(" ", "\\ ").Replace("'", "\\'").Replace("&", "\\&");
                            string finalActor = actor.Replace(" ", "\\ ").Replace("'", "\\'").Replace("&", "\\&");
                            string output = "ln -s " + sourcePathActors + SortTitleToFolder(record.folderName) + "/" + finalFolderName + " " + destinationPathActors + finalActor;
                            sw.WriteLine(output);
                            Console.WriteLine(output);
                        }
                    }
                }
            }
            #endregion

            sw.Close();
        }

        // Method, needed to correctly make a shell script with correct paths to movie files; this will be different for other cases/systems
        public static string SortTitleToFolder(string title)
        {
            char firstLetterOfTitle = title[0];
            string output = "";

            if(firstLetterOfTitle == '0' || firstLetterOfTitle == '1' || firstLetterOfTitle == '2' || firstLetterOfTitle == '3' || firstLetterOfTitle == '4' || firstLetterOfTitle == '5' || firstLetterOfTitle == '6' || firstLetterOfTitle == '7' || firstLetterOfTitle == '8' || firstLetterOfTitle == '9')
            {
                output = "0-9";
            }
            else if (firstLetterOfTitle == 'A' || firstLetterOfTitle == 'B' || firstLetterOfTitle == 'C')
            {
                output = "ABC";
            }
            else if (firstLetterOfTitle == 'D' || firstLetterOfTitle == 'E' || firstLetterOfTitle == 'F')
            {
                output = "DEF";
            }
            else if (firstLetterOfTitle == 'G' || firstLetterOfTitle == 'H' || firstLetterOfTitle == 'I')
            {
                output = "GHI";
            }
            else if (firstLetterOfTitle == 'J' || firstLetterOfTitle == 'K' || firstLetterOfTitle == 'L')
            {
                output = "JKL";
            }
            else if (firstLetterOfTitle == 'M' || firstLetterOfTitle == 'N' || firstLetterOfTitle == 'O')
            {
                output = "MNO";
            }
            else if (firstLetterOfTitle == 'P' || firstLetterOfTitle == 'Q' || firstLetterOfTitle == 'R' || firstLetterOfTitle == 'S')
            {
                output = "PQRS";
            }
            else if (firstLetterOfTitle == 'T' || firstLetterOfTitle == 'U' || firstLetterOfTitle == 'V')
            {
                output = "TUV";
            }
            else if (firstLetterOfTitle == 'W' || firstLetterOfTitle == 'X' || firstLetterOfTitle == 'Y' || firstLetterOfTitle == 'Z')
            {
                output = "WXYZ";
            }
            return output;
        }
    }
}
