using Backend.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Backend.Data;

public static class DatabaseSeeder
{
    public static async Task SeedAsync(IServiceProvider serviceProvider)
    {
        var context = serviceProvider.GetRequiredService<ApplicationDbContext>();
        var userManager = serviceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var roleManager = serviceProvider.GetRequiredService<RoleManager<IdentityRole>>();

        await context.Database.MigrateAsync();

        await SeedRoles(roleManager);

        await SeedUsers(userManager);

        await SeedJobOffers(context);
    }

    public static async Task RefreshSeedAsync(IServiceProvider serviceProvider)
    {
        var context = serviceProvider.GetRequiredService<ApplicationDbContext>();
        var userManager = serviceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var roleManager = serviceProvider.GetRequiredService<RoleManager<IdentityRole>>();

        var applications = await context.JobApplications.ToListAsync();
        context.JobApplications.RemoveRange(applications);
        await context.SaveChangesAsync();

        var jobOffers = await context.JobOffers.ToListAsync();
        context.JobOffers.RemoveRange(jobOffers);
        await context.SaveChangesAsync();

        var users = await userManager.Users.ToListAsync();
        foreach (var user in users)
        {
            await userManager.DeleteAsync(user);
        }

        await SeedRoles(roleManager);

        await SeedUsers(userManager);

        await SeedJobOffers(context, forceReseed: true);
    }

    private static async Task SeedRoles(RoleManager<IdentityRole> roleManager)
    {
        string[] roles = { "Admin", "User" };

        foreach (var role in roles)
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                await roleManager.CreateAsync(new IdentityRole(role));
            }
        }
    }

    private static async Task SeedUsers(UserManager<ApplicationUser> userManager)
    {
        var admins = new[]
        {
            new { Email = "admin@jobboard.com", FirstName = "Admin", LastName = "Główny", Password = "Admin123!" },
            new { Email = "superadmin@jobboard.com", FirstName = "Super", LastName = "Admin", Password = "Admin123!" }
        };

        foreach (var adminData in admins)
        {
            if (await userManager.FindByEmailAsync(adminData.Email) == null)
            {
                var admin = new ApplicationUser
                {
                    UserName = adminData.Email,
                    Email = adminData.Email,
                    FirstName = adminData.FirstName,
                    LastName = adminData.LastName,
                    EmailConfirmed = true,
                    CreatedAt = DateTime.UtcNow
                };

                var result = await userManager.CreateAsync(admin, adminData.Password);
                if (result.Succeeded)
                {
                    await userManager.AddToRoleAsync(admin, "Admin");
                }
            }
        }

        var regularUsers = new[]
        {
            new { Email = "jan.kowalski@example.com", FirstName = "Jan", LastName = "Kowalski", Password = "User123!" },
            new { Email = "anna.nowak@example.com", FirstName = "Anna", LastName = "Nowak", Password = "User123!" },
            new { Email = "piotr.wisniewski@example.com", FirstName = "Piotr", LastName = "Wiśniewski", Password = "User123!" },
            new { Email = "maria.wojcik@example.com", FirstName = "Maria", LastName = "Wójcik", Password = "User123!" },
            new { Email = "tomasz.kaminski@example.com", FirstName = "Tomasz", LastName = "Kamiński", Password = "User123!" }
        };

        foreach (var userData in regularUsers)
        {
            if (await userManager.FindByEmailAsync(userData.Email) == null)
            {
                var user = new ApplicationUser
                {
                    UserName = userData.Email,
                    Email = userData.Email,
                    FirstName = userData.FirstName,
                    LastName = userData.LastName,
                    EmailConfirmed = true,
                    CreatedAt = DateTime.UtcNow
                };

                var result = await userManager.CreateAsync(user, userData.Password);
                if (result.Succeeded)
                {
                    await userManager.AddToRoleAsync(user, "User");
                }
            }
        }
    }

    private static async Task SeedJobOffers(ApplicationDbContext context, bool forceReseed = false)
    {
        if (!forceReseed && await context.JobOffers.AnyAsync())
        {
            return;
        }

        var jobOffers = new List<JobOffer>
        {
            new JobOffer
            {
                Title = "Senior .NET Developer",
                Company = "TechCorp Solutions",
                Description = "Poszukujemy doświadczonego programisty .NET do pracy nad aplikacjami enterprise. Wymagana znajomość ASP.NET Core, Entity Framework, oraz wzorców projektowych.",
                SalaryMin = 15000,
                SalaryMax = 22000,
                Location = "Warszawa",
                WorkType = WorkType.Hybrid,
                CreatedAt = DateTime.UtcNow.AddDays(-10)
            },
            new JobOffer
            {
                Title = "Frontend Developer - Angular",
                Company = "Digital Innovations",
                Description = "Szukamy kreatywnego Frontend Developera z doświadczeniem w Angular 15+. Praca nad nowoczesnymi aplikacjami SPA.",
                SalaryMin = 10000,
                SalaryMax = 16000,
                Location = "Kraków",
                WorkType = WorkType.Remote,
                CreatedAt = DateTime.UtcNow.AddDays(-9)
            },
            new JobOffer
            {
                Title = "Full Stack Developer (React + Node.js)",
                Company = "StartupHub",
                Description = "Dołącz do dynamicznego zespołu! Budujemy nowoczesne aplikacje webowe używając React i Node.js. Mile widziana znajomość TypeScript.",
                SalaryMin = 12000,
                SalaryMax = 18000,
                Location = "Wrocław",
                WorkType = WorkType.Hybrid,
                CreatedAt = DateTime.UtcNow.AddDays(-8)
            },
            new JobOffer
            {
                Title = "DevOps Engineer",
                Company = "CloudMasters",
                Description = "Poszukujemy DevOps Engineera do zarządzania infrastrukturą chmurową. Doświadczenie z AWS, Docker, Kubernetes.",
                SalaryMin = 14000,
                SalaryMax = 20000,
                Location = "Gdańsk",
                WorkType = WorkType.Remote,
                CreatedAt = DateTime.UtcNow.AddDays(-7)
            },
            new JobOffer
            {
                Title = "Java Backend Developer",
                Company = "Enterprise Systems",
                Description = "Dołącz do zespołu backend! Praca z Spring Boot, Hibernate, microservices. Stabilna firma, ciekawe projekty.",
                SalaryMin = 13000,
                SalaryMax = 19000,
                Location = "Poznań",
                WorkType = WorkType.Onsite,
                CreatedAt = DateTime.UtcNow.AddDays(-6)
            },
            new JobOffer
            {
                Title = "Mobile Developer - Flutter",
                Company = "MobileFirst Apps",
                Description = "Twórz aplikacje mobilne na Android i iOS używając Flutter. Doświadczenie z Dart i Firebase mile widziane.",
                SalaryMin = 11000,
                SalaryMax = 17000,
                Location = "Łódź",
                WorkType = WorkType.Remote,
                CreatedAt = DateTime.UtcNow.AddDays(-5)
            },
            new JobOffer
            {
                Title = "Python Data Engineer",
                Company = "DataDriven Analytics",
                Description = "Buduj pipeline'y danych i systemy ETL. Wymagana znajomość Python, Pandas, SQL, Apache Airflow.",
                SalaryMin = 14000,
                SalaryMax = 21000,
                Location = "Warszawa",
                WorkType = WorkType.Hybrid,
                CreatedAt = DateTime.UtcNow.AddDays(-4)
            },
            new JobOffer
            {
                Title = "UI/UX Designer",
                Company = "Creative Studios",
                Description = "Projektuj intuicyjne interfejsy dla aplikacji webowych i mobilnych. Znajomość Figma, Adobe XD. Portfolio wymagane.",
                SalaryMin = 9000,
                SalaryMax = 14000,
                Location = "Kraków",
                WorkType = WorkType.Hybrid,
                CreatedAt = DateTime.UtcNow.AddDays(-3)
            },
            new JobOffer
            {
                Title = "QA Automation Engineer",
                Company = "QualityFirst",
                Description = "Automatyzacja testów w Selenium/Cypress. Praca w zespole Agile. Doświadczenie z CI/CD.",
                SalaryMin = 10000,
                SalaryMax = 15000,
                Location = "Wrocław",
                WorkType = WorkType.Remote,
                CreatedAt = DateTime.UtcNow.AddDays(-2)
            },
            new JobOffer
            {
                Title = "Scrum Master",
                Company = "Agile Experts",
                Description = "Prowadź zespoły scrumowe do sukcesu! Certyfikat CSM/PSM wymagany. Doświadczenie min. 2 lata.",
                SalaryMin = 12000,
                SalaryMax = 17000,
                Location = "Gdańsk",
                WorkType = WorkType.Hybrid,
                CreatedAt = DateTime.UtcNow.AddDays(-1)
            },
            new JobOffer
            {
                Title = "Cybersecurity Specialist",
                Company = "SecureIT",
                Description = "Chroń systemy przed zagrożeniami! Penetration testing, security audits, incident response.",
                SalaryMin = 16000,
                SalaryMax = 24000,
                Location = "Warszawa",
                WorkType = WorkType.Onsite,
                CreatedAt = DateTime.UtcNow
            },
            new JobOffer
            {
                Title = "PHP Laravel Developer",
                Company = "WebSolutions",
                Description = "Rozwijaj aplikacje webowe w Laravel. REST API, MySQL, Redis. Praca w małym, zgranym zespole.",
                SalaryMin = 9000,
                SalaryMax = 14000,
                Location = "Katowice",
                WorkType = WorkType.Remote,
                CreatedAt = DateTime.UtcNow.AddHours(-12)
            },
            new JobOffer
            {
                Title = "React Native Developer",
                Company = "CrossPlatform Studios",
                Description = "Twórz aplikacje mobilne cross-platform. Znajomość React Native, Redux, TypeScript.",
                SalaryMin = 11000,
                SalaryMax = 16000,
                Location = "Poznań",
                WorkType = WorkType.Remote,
                CreatedAt = DateTime.UtcNow.AddHours(-10)
            },
            new JobOffer
            {
                Title = "Database Administrator (PostgreSQL)",
                Company = "DataOps Inc",
                Description = "Zarządzaj bazami danych PostgreSQL. Performance tuning, backup strategies, high availability.",
                SalaryMin = 13000,
                SalaryMax = 19000,
                Location = "Wrocław",
                WorkType = WorkType.Hybrid,
                CreatedAt = DateTime.UtcNow.AddHours(-8)
            },
            new JobOffer
            {
                Title = "Machine Learning Engineer",
                Company = "AI Innovations",
                Description = "Twórz modele ML i wdrażaj je do produkcji. Python, TensorFlow, PyTorch, MLOps.",
                SalaryMin = 17000,
                SalaryMax = 25000,
                Location = "Warszawa",
                WorkType = WorkType.Hybrid,
                CreatedAt = DateTime.UtcNow.AddHours(-6)
            },
            new JobOffer
            {
                Title = "Blockchain Developer",
                Company = "CryptoTech",
                Description = "Rozwijaj aplikacje blockchain. Solidity, Ethereum, Smart Contracts. Znajomość Web3.js.",
                SalaryMin = 15000,
                SalaryMax = 23000,
                Location = "Remote",
                WorkType = WorkType.Remote,
                CreatedAt = DateTime.UtcNow.AddHours(-4)
            },
            new JobOffer
            {
                Title = "System Administrator Linux",
                Company = "Infrastructure Masters",
                Description = "Administracja serwerami Linux. Bash scripting, monitoring, automation. Doświadczenie z Ansible mile widziane.",
                SalaryMin = 11000,
                SalaryMax = 16000,
                Location = "Gdańsk",
                WorkType = WorkType.Onsite,
                CreatedAt = DateTime.UtcNow.AddHours(-3)
            },
            new JobOffer
            {
                Title = "Business Intelligence Analyst",
                Company = "DataInsights Corp",
                Description = "Twórz raporty i dashboardy w Power BI/Tableau. Analiza danych biznesowych, SQL, DAX.",
                SalaryMin = 10000,
                SalaryMax = 15000,
                Location = "Kraków",
                WorkType = WorkType.Hybrid,
                CreatedAt = DateTime.UtcNow.AddHours(-2)
            },
            new JobOffer
            {
                Title = "Embedded Systems Developer",
                Company = "IoT Solutions",
                Description = "Programowanie systemów wbudowanych. C/C++, ARM, RTOS. Praca nad urządzeniami IoT.",
                SalaryMin = 12000,
                SalaryMax = 18000,
                Location = "Gliwice",
                WorkType = WorkType.Onsite,
                CreatedAt = DateTime.UtcNow.AddHours(-1)
            },
            new JobOffer
            {
                Title = "Technical Project Manager",
                Company = "ProjectPro",
                Description = "Zarządzaj projektami IT od A do Z. Znajomość metodyk Agile i Waterfall. Doświadczenie z Jira, Confluence.",
                SalaryMin = 14000,
                SalaryMax = 20000,
                Location = "Warszawa",
                WorkType = WorkType.Hybrid,
                CreatedAt = DateTime.UtcNow.AddMinutes(-30)
            }
        };

        await context.JobOffers.AddRangeAsync(jobOffers);
        await context.SaveChangesAsync();
    }
}
