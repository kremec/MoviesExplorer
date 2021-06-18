using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MongoDB.Bson.Serialization.Attributes;

namespace MoviesExplorer
{
    class Movie
    {
        [BsonId]
        public Guid Id { get; set; } // _id

        public OriginalMovieData OriginalMovieData { get; set; }

        public string folderName { get; set; }

        public string title { get; set; }
        public int year { get; set; }
        public string genre { get; set; }
        public string actors { get; set; }

        public bool possibleError { get; set; }

    }

    public class OriginalMovieData
    {
        public string originalTitle { get; set; }
        public int originalYear { get; set; }
        public string originalGenre { get; set; }
        public string originalActors { get; set; }
    }
}
