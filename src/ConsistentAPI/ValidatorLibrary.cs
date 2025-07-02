using System.Net.Mail;

namespace ConsistentAPI;

public static class ValidatorLibrary
{
  public static bool IsValidEmail(string email)
  {
    try
    {
      var mailAddress = new MailAddress(email.Trim());
      return mailAddress.Address == email.Trim();
    }
    catch (FormatException)
    {
      return false;
    }
  }
}
