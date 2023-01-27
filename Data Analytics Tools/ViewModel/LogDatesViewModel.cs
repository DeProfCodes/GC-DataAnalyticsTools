using System;
using System.ComponentModel.DataAnnotations;

namespace Data_Analytics_Tools.ViewModel
{
    public class LogDatesViewModel
    {
        [DisplayFormat(ApplyFormatInEditMode = true, DataFormatString = "{0:MM/dd/yyyy}")]
        [DataType(DataType.Date)]
        public DateTime StartDate { get; set; }

        [DisplayFormat(ApplyFormatInEditMode = true, DataFormatString = "{0:MM/dd/yyyy}")]
        [DataType(DataType.Date)]
        public DateTime EndDate { get; set; }
    }
}
