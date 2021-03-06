﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Mail;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using ShaneMaravillo.SchoolFacilities.Web.Infrastructures.Data.Enums;
using ShaneMaravillo.SchoolFacilities.Web.Infrastructures.Data.Helpers;
using ShaneMaravillo.SchoolFacilities.Web.Infrastructures.Data.Models;
using ShaneMaravillo.SchoolFacilities.Web.Infrastructures.Data.Security;
using ShaneMaravillo.SchoolFacilities.Web.ViewModels.Account;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace ShaneMaravillo.SchoolFacilities.Web.Controllers
{
    public class AccountController : Controller
    {
        private readonly DefaultDbContext _context;
        protected readonly IConfiguration _config;
        private string emailUserName;
        private string emailPassword;
        private IHostingEnvironment _env;


        public AccountController(DefaultDbContext context, IConfiguration iConfiguration, IHostingEnvironment env)
        {
            _context = context;
            this._config = iConfiguration;
            var emailConfig = this._config.GetSection("Email");
            emailUserName = (emailConfig["Username"]).ToString();
            emailPassword = (emailConfig["Password"]).ToString();
            _env = env;

        }

        [HttpGet, Route("Account/Register")]
        public IActionResult Register()
        {
            return View();
        }

        [HttpPost, Route("account/register")]
        public IActionResult Register(RegisterViewModel model)
        {
            if (model.Password != model.ConfirmPassword)
            {
                ModelState.AddModelError("", "Password and Confirmation does not match.");
                return View();
            }
            var duplicate = this._context.Users.FirstOrDefault(u => u.EmailAddress.ToLower() == model.EmailAddress.ToLower());
            if (duplicate == null)
            {
                var registrationCode = RandomString(6);
                User user = new User()
                {
                    Id = Guid.NewGuid(),
                    FirstName = model.FirstName,
                    LastName = model.LastName,
                    EmailAddress = model.EmailAddress.ToLower(),
                    Gender = model.Gender,
                    LoginStatus = Infrastructures.Data.Enums.LoginStatus.NewRegister,
                    RegistrationCode = registrationCode,
                    Password = BCrypt.BCryptHelper.HashPassword(model.Password, BCrypt.BCryptHelper.GenerateSalt(8))
                };
                this._context.Users.Add(user);
                this._context.SaveChanges();
                this.SendNow(
                           "Hi " + model.FirstName + " " + model.LastName + @",
                             Welcome to CSM Bataan Website. Please use the following registration code to activate your account: " + registrationCode + @".
                             Regards,
                             CSM Bataan Website",
                           model.EmailAddress,
                           model.FirstName + " " + model.LastName,
                           "Welcome to SchoolFacilities CSM Bataan Website!!!"
               );
            }
            return View();
        }
        [HttpGet, Route("account/verify")]
        public IActionResult Verify()
        {
            return View();
        }
        [HttpPost, Route("account/verify")]
        public IActionResult Verify(VerifyViewModel model)
        {
            var user = this._context.Users.FirstOrDefault(u => u.EmailAddress.ToLower() == model.EmailAddress.ToLower() && u.RegistrationCode == model.RegistrationCode);
            if (user != null)
            {
                user.LoginStatus = Infrastructures.Data.Enums.LoginStatus.Active;
                user.LoginTrials = 0;
                this._context.Users.Update(user);
                this._context.SaveChanges();
                return RedirectToAction("login");
            }
            return View();
        }

        [HttpGet, Route("account/login")]
        public IActionResult Login()
        {
            return View();
        }
        [HttpPost, Route("account/login")]
        public async Task<IActionResult> Login(LoginViewModel model)
        {
            var user = this._context.Users.FirstOrDefault(u =>
                u.EmailAddress.ToLower() == model.EmailAddress.ToLower());
            if (user != null)
            {
                if (BCrypt.BCryptHelper.CheckPassword(model.Password, user.Password))
                {
                    if (user.LoginStatus == Infrastructures.Data.Enums.LoginStatus.Locked)
                    {
                        ModelState.AddModelError("", "Your account has been locked please contact an Administrator.");
                        return View();
                    }
                    else if (user.LoginStatus == Infrastructures.Data.Enums.LoginStatus.NewRegister)
                    {
                        ModelState.AddModelError("", "Please verify your account first.");
                        return View();
                    }
                    else if (user.LoginStatus == Infrastructures.Data.Enums.LoginStatus.NeedsToChangePassword)
                    {
                        user.LoginTrials = 0;
                        user.LoginStatus = Infrastructures.Data.Enums.LoginStatus.Active;
                        this._context.Users.Update(user);
                        this._context.SaveChanges();

                        WebUser.SetUser(user, GetRoles(user.Id));
                        await this.SignIn();

                        return RedirectToAction("change-password");
                    }
                    else if (user.LoginStatus == Infrastructures.Data.Enums.LoginStatus.Active)
                    {
                        user.LoginTrials = 0;
                        user.LoginStatus = Infrastructures.Data.Enums.LoginStatus.Active;
                        this._context.Users.Update(user);
                        this._context.SaveChanges();

                        WebUser.SetUser(user, GetRoles(user.Id));
                        await this.SignIn();

                        return RedirectPermanent("/threads/index");
                    }
                }
                else
                {
                    user.LoginTrials = user.LoginTrials + 1;
                    if (user.LoginTrials >= 3)
                    {
                        ModelState.AddModelError("", "Your account has been locked please contact an Administrator.");
                        user.LoginStatus = Infrastructures.Data.Enums.LoginStatus.Locked;
                    }
                    this._context.Users.Update(user);
                    this._context.SaveChanges();


                    ModelState.AddModelError("", "Invalid Login.");
                    return View();
                }
            }
            ModelState.AddModelError("", "Invalid Login.");
            return View();
        }

        private List<Role> GetRoles(Guid? userId)
        {
            List<Role> roles = new List<Role>();
            var userRoles = this._context.UserRoles.Where(r => r.UserId == userId);

            foreach (UserRole userRole in userRoles)
            {
                roles.Add(userRole.Role);
            }


            return roles;
        }


        [HttpGet, Route("account/forgot-password")]
        public IActionResult ForgotPassword()
        {
            return View();
        }
        [HttpPost, Route("account/forgot-password")]
        public IActionResult ForgotPassword(ForgotPasswordViewModel model)
        {
            var user = this._context.Users.FirstOrDefault(u =>
                    u.EmailAddress.ToLower() == model.EmailAddress.ToLower());
            if (user != null)
            {
                var newPassword = RandomString(6);
                user.Password = BCrypt.BCryptHelper.HashPassword(newPassword, BCrypt.BCryptHelper.GenerateSalt(8));
                user.LoginStatus = Infrastructures.Data.Enums.LoginStatus.NeedsToChangePassword;
                this._context.Users.Update(user);
                this._context.SaveChanges();
                this.SendNow(
                           "Hi " + user.FirstName + " " + user.LastName + @",
                             You forgot your password. Please use this new password: " + newPassword + @".
                             Regards,
                             CSM Bataan Website",
                           user.EmailAddress,
                           user.FirstName + " " + user.LastName,
                           "CSM Bataan Website - Forgot Password"
               );
            }
            return View();
        }

        [Authorize(Policy = "SignedIn")]
        [HttpGet, Route("account/change-password")]
        public IActionResult ChangePassword()
        {
            return View();
        }

        [Authorize(Policy = "SignedIn")]
        [HttpPost, Route("account/change-password")]
        public IActionResult ChangePassword(ChangePasswordViewModel model)
        {
            if (model.NewPassword != model.ConfirmNewPassword)
            {
                ModelState.AddModelError("", "New Password does not match Confirm New Password");
                return View();
            }
            var user = this._context.Users.FirstOrDefault(u =>
                   u.Id == WebUser.UserId);
            if (user != null)
            {
                if (BCrypt.BCryptHelper.CheckPassword(model.OldPassword, user.Password) == false)
                {
                    ModelState.AddModelError("", "Incorrect old Password.");
                    return View();
                }
                user.Password = BCrypt.BCryptHelper.HashPassword(model.NewPassword, BCrypt.BCryptHelper.GenerateSalt(8));
                user.LoginStatus = Infrastructures.Data.Enums.LoginStatus.Active;
                this._context.Users.Update(user);
                this._context.SaveChanges();
                return RedirectPermanent("/home/index");
            }
            return View();
        }

        [Authorize(Policy = "SignedIn")]
        [HttpGet, Route("account/update-profile")]
        public IActionResult UpdateProfile()
        {
            return View(new UpdateProfileViewModel()
            {
                FirstName = WebUser.FirstName,
                LastName = WebUser.LastName,
                UserId = WebUser.UserId
            });
        }
        [Authorize(Policy = "SignedIn")]
        [HttpPost, Route("account/update-profile")]
        public IActionResult UpdateProfile(UpdateProfileViewModel model)
        {
            var user = this._context.Users.FirstOrDefault(u =>
                    u.Id == WebUser.UserId);
            if (user != null)
            {
                user.FirstName = model.FirstName;
                user.LastName = model.LastName;
                this._context.Users.Update(user);
                this._context.SaveChanges();
                WebUser.FirstName = model.FirstName;
                WebUser.LastName = model.LastName;
                return RedirectPermanent("/home/index");
            }
            return View();
        }


        [Authorize(Policy = "SignedIn")]
        [HttpGet, Route("account/update-profile-image")]
        public IActionResult UpdateProfileImage()
        {
            return View();
        }
        [Authorize(Policy = "SignedIn")]
        [HttpPost, Route("account/update-profile-image")]
        public async Task<IActionResult> UpdateProfileImage(ProfileImageViewModel model)
        {
            var fileSize = model.Image.Length;
            if ((fileSize / 1048576.0) > 2)
            {
                ModelState.AddModelError("", "The file you uploaded is too large. Filesize limit is 2mb.");
                return View(model);
            }
            if (model.Image.ContentType != "image/jpeg" && model.Image.ContentType != "image/png")
            {
                ModelState.AddModelError("", "Please upload a jpeg or png file for the thumbnail.");
                return View(model);
            }
            var dirPath = _env.WebRootPath + "/users/";
            if (!Directory.Exists(dirPath))
            {
                Directory.CreateDirectory(dirPath);
            }
            var filePath = dirPath + WebUser.UserId.ToString() + ".png";
            if (model.Image.Length > 0)
            {
                byte[] bytes = await FileBytes(model.Image.OpenReadStream());
                using (Image<Rgba32> image = Image.Load(bytes))
                {
                    image.Mutate(x => x.Resize(150, 150));
                    image.Save(filePath);
                }
            }
            return RedirectToAction("UpdateProfileImage");
        }
        public async Task<byte[]> FileBytes(Stream input)
        {
            byte[] buffer = new byte[16 * 1024];
            using (MemoryStream ms = new MemoryStream())
            {
                int read;
                while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                {
                    ms.Write(buffer, 0, read);
                }
                return ms.ToArray();
            }
        }



        /// ////////////////////////////////////////


        private async Task SignIn()
        {
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, WebUser.UserId.ToString())
            };
            ClaimsIdentity identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            ClaimsPrincipal principal = new ClaimsPrincipal(identity);
            var authProperties = new AuthenticationProperties
            {
                AllowRefresh = true,
                ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(10),
                IsPersistent = true,
                IssuedUtc = DateTimeOffset.UtcNow
            };
            await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal, authProperties);
        }
        private async Task SignOut()
        {
            await HttpContext.SignOutAsync();
            WebUser.EmailAddress = string.Empty;
            WebUser.FirstName = string.Empty;
            WebUser.LastName = string.Empty;
            WebUser.UserId = null;
            WebUser.Roles = null;



            HttpContext.Session.Clear();
        }








        private Random random = new Random();
        private string RandomString(int length)
        {
            const string chars = "abcdefghijklmnopqrstuvwxyz0123456789";
            return new string(Enumerable.Repeat(chars, length)
              .Select(s => s[random.Next(s.Length)]).ToArray());
        }
        private void SendNow(string message, string messageTo, string messageName, string emailSubject)
        {
            var fromAddress = new MailAddress(emailUserName, "CSM Bataan Apps");
            string body = message;
            ///https://support.google.com/accounts/answer/6010255?hl=en
            var smtp = new SmtpClient
            {
                Host = "smtp.gmail.com",
                Port = 587,
                EnableSsl = true,
                DeliveryMethod = SmtpDeliveryMethod.Network,
                UseDefaultCredentials = false,
                Credentials = new NetworkCredential(fromAddress.Address, emailPassword),
                Timeout = 20000
            };
            var toAddress = new MailAddress(messageTo, messageName);
            smtp.Send(new MailMessage(fromAddress, toAddress)
            {
                Subject = emailSubject,
                Body = body,
                IsBodyHtml = true
            });
        }
    }
} 
