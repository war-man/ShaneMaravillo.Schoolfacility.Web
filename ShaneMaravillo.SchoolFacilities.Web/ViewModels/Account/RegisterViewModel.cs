﻿using ShaneMaravillo.SchoolFacilities.Web.Infrastructures.Data.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ShaneMaravillo.SchoolFacilities.Web.ViewModels.Account
{
    public class RegisterViewModel
    {
        public string FirstName { get; set; }

        public string LastName { get; set; }

        public Gender Gender { get; set; }

        public string EmailAddress { get; set; }

        public string ConfirmPassword { get; set; }

        public string Password { get; set; }

    }
}

