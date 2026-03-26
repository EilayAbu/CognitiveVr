namespace CognitiveVR.Models
{
    [System.Serializable]
    public class PatientProfile
    {
        public string PatientId;
        public string FirstName;
        public string LastName;
        public int Age;
        public string Therapist;
        public System.DateTime SessionDate;
        public string Notes;

        public string FullName => $"{FirstName} {LastName}";
    }
}
