using System.ComponentModel.DataAnnotations;
using Backend.Models;

namespace Backend.DTOs;

public class UpdateJobOfferStatusDto
{
    [Required]
    [EnumDataType(typeof(JobOfferStatus))]
    public JobOfferStatus? Status { get; set; }
}
