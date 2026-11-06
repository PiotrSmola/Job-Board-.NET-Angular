using Backend.Helpers;
using Backend.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Backend.Data;

public static class DatabaseSeeder
{
    // konta demo pracodawców (README): zatwierdzona firma z ofertą z seeda i firma oczekująca bez ofert
    public const string ApprovedEmployerEmail = "rekruter@techcorp.pl";
    public const string ApprovedEmployerCompany = "TechCorp Solutions";
    public const string PendingEmployerEmail = "rekruter@nowafirma.pl";
    public const string PendingEmployerCompany = "Nowa Firma Sp. z o.o.";
    public const string EmployerPassword = "Employer123!";

    // Konta demo (kandydaci i pracodawcy ze znanymi hasłami) tylko lokalnie: domyślnie w Development,
    // poza nim po jawnym włączeniu Seed:DemoAccounts=true (lokalny stack Dockera); jawna wartość ma pierwszeństwo.
    // Konta adminów są tworzone zawsze.
    public static bool DemoAccountsEnabled(IHostEnvironment environment, IConfiguration configuration) =>
        configuration.GetValue<bool?>("Seed:DemoAccounts") ?? environment.IsDevelopment();

    public static async Task SeedAsync(IServiceProvider serviceProvider)
    {
        var context = serviceProvider.GetRequiredService<ApplicationDbContext>();
        var userManager = serviceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var roleManager = serviceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        var includeDemoAccounts = DemoAccountsEnabled(
            serviceProvider.GetRequiredService<IHostEnvironment>(),
            serviceProvider.GetRequiredService<IConfiguration>());

        await context.Database.MigrateAsync();

        await SeedRoles(roleManager);

        await SeedAdmins(userManager);

        var categoriesSeeded = await SeedCategories(context);

        await SeedJobOffers(context);

        // baza sprzed taksonomii: kategorie dopiero powstały, a istniejące oferty demo nie mają jeszcze kategorii
        // ani umiejętności - uzupełniamy je (tylko oferty bez żadnej taksonomii, dopasowane po tytule z seeda)
        if (categoriesSeeded)
        {
            await BackfillSeedTaxonomy(context);
        }

        if (includeDemoAccounts)
        {
            await SeedCandidates(userManager);
            await SeedEmployers(context, userManager);
        }
    }

    // Usuwa WSZYSTKIE aplikacje, oferty, użytkowników, firmy, kategorie i umiejętności, po czym odtwarza dane demo.
    // UserManager i RoleManager muszą korzystać z tego samego (scoped) kontekstu - wtedy całość jest w jednej transakcji.
    public static async Task RefreshSeedAsync(
        ApplicationDbContext context,
        UserManager<ApplicationUser> userManager,
        RoleManager<IdentityRole> roleManager,
        bool includeDemoAccounts)
    {
        await using var transaction = await context.Database.BeginTransactionAsync();

        await context.JobApplications.ExecuteDeleteAsync();
        await context.JobOffers.ExecuteDeleteAsync();

        // powiązania ofert z umiejętnościami zniknęły razem z ofertami (kaskada); kategorie i umiejętności wracają do zestawu demo
        await context.Skills.ExecuteDeleteAsync();
        await context.Categories.ExecuteDeleteAsync();

        var users = await userManager.Users.ToListAsync();
        foreach (var user in users)
        {
            await userManager.DeleteAsync(user);
        }

        // firmy po użytkownikach - konta pracodawców wskazują firmę (klucz obcy Restrict)
        await context.Companies.ExecuteDeleteAsync();

        await SeedRoles(roleManager);

        await SeedAdmins(userManager);

        await SeedCategories(context);

        await SeedJobOffers(context, forceReseed: true);

        if (includeDemoAccounts)
        {
            await SeedCandidates(userManager);
            await SeedEmployers(context, userManager);
        }

        await transaction.CommitAsync();
    }

    private static async Task SeedRoles(RoleManager<IdentityRole> roleManager)
    {
        foreach (var role in Roles.All)
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                await roleManager.CreateAsync(new IdentityRole(role));
            }
        }
    }

    private static async Task SeedAdmins(UserManager<ApplicationUser> userManager)
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
    }

    // Konta demo kandydatów (rola User)
    private static async Task SeedCandidates(UserManager<ApplicationUser> userManager)
    {
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

        var jobOffers = new List<(string Company, JobOffer Offer)>
        {
            ("TechCorp Solutions", new JobOffer
            {
                Title = "Senior .NET Developer",
                Description = "Poszukujemy doświadczonego programisty .NET do pracy nad aplikacjami enterprise. Wymagana znajomość ASP.NET Core, Entity Framework, oraz wzorców projektowych.",
                SalaryMin = 15000,
                SalaryMax = 22000,
                Location = "Warszawa",
                WorkType = WorkType.Hybrid,
                CreatedAt = DateTime.UtcNow.AddDays(-10)
            }),
            ("Digital Innovations", new JobOffer
            {
                Title = "Frontend Developer - Angular",
                Description = "Szukamy kreatywnego Frontend Developera z doświadczeniem w Angular 15+. Praca nad nowoczesnymi aplikacjami SPA.",
                SalaryMin = 10000,
                SalaryMax = 16000,
                Location = "Kraków",
                WorkType = WorkType.Remote,
                CreatedAt = DateTime.UtcNow.AddDays(-9)
            }),
            ("StartupHub", new JobOffer
            {
                Title = "Full Stack Developer (React + Node.js)",
                Description = "Dołącz do dynamicznego zespołu! Budujemy nowoczesne aplikacje webowe używając React i Node.js. Mile widziana znajomość TypeScript.",
                SalaryMin = 12000,
                SalaryMax = 18000,
                Location = "Wrocław",
                WorkType = WorkType.Hybrid,
                CreatedAt = DateTime.UtcNow.AddDays(-8)
            }),
            ("CloudMasters", new JobOffer
            {
                Title = "DevOps Engineer",
                Description = "Poszukujemy DevOps Engineera do zarządzania infrastrukturą chmurową. Doświadczenie z AWS, Docker, Kubernetes.",
                SalaryMin = 14000,
                SalaryMax = 20000,
                Location = "Gdańsk",
                WorkType = WorkType.Remote,
                CreatedAt = DateTime.UtcNow.AddDays(-7)
            }),
            ("Enterprise Systems", new JobOffer
            {
                Title = "Java Backend Developer",
                Description = "Dołącz do zespołu backend! Praca z Spring Boot, Hibernate, microservices. Stabilna firma, ciekawe projekty.",
                SalaryMin = 13000,
                SalaryMax = 19000,
                Location = "Poznań",
                WorkType = WorkType.Onsite,
                CreatedAt = DateTime.UtcNow.AddDays(-6)
            }),
            ("MobileFirst Apps", new JobOffer
            {
                Title = "Mobile Developer - Flutter",
                Description = "Twórz aplikacje mobilne na Android i iOS używając Flutter. Doświadczenie z Dart i Firebase mile widziane.",
                SalaryMin = 11000,
                SalaryMax = 17000,
                Location = "Łódź",
                WorkType = WorkType.Remote,
                CreatedAt = DateTime.UtcNow.AddDays(-5)
            }),
            ("DataDriven Analytics", new JobOffer
            {
                Title = "Python Data Engineer",
                Description = "Buduj pipeline'y danych i systemy ETL. Wymagana znajomość Python, Pandas, SQL, Apache Airflow.",
                SalaryMin = 14000,
                SalaryMax = 21000,
                Location = "Warszawa",
                WorkType = WorkType.Hybrid,
                CreatedAt = DateTime.UtcNow.AddDays(-4)
            }),
            ("Creative Studios", new JobOffer
            {
                Title = "UI/UX Designer",
                Description = "Projektuj intuicyjne interfejsy dla aplikacji webowych i mobilnych. Znajomość Figma, Adobe XD. Portfolio wymagane.",
                SalaryMin = 9000,
                SalaryMax = 14000,
                Location = "Kraków",
                WorkType = WorkType.Hybrid,
                CreatedAt = DateTime.UtcNow.AddDays(-3)
            }),
            ("QualityFirst", new JobOffer
            {
                Title = "QA Automation Engineer",
                Description = "Automatyzacja testów w Selenium/Cypress. Praca w zespole Agile. Doświadczenie z CI/CD.",
                SalaryMin = 10000,
                SalaryMax = 15000,
                Location = "Wrocław",
                WorkType = WorkType.Remote,
                CreatedAt = DateTime.UtcNow.AddDays(-2)
            }),
            ("Agile Experts", new JobOffer
            {
                Title = "Scrum Master",
                Description = "Prowadź zespoły scrumowe do sukcesu! Certyfikat CSM/PSM wymagany. Doświadczenie min. 2 lata.",
                SalaryMin = 12000,
                SalaryMax = 17000,
                Location = "Gdańsk",
                WorkType = WorkType.Hybrid,
                CreatedAt = DateTime.UtcNow.AddDays(-1)
            }),
            ("SecureIT", new JobOffer
            {
                Title = "Cybersecurity Specialist",
                Description = "Chroń systemy przed zagrożeniami! Penetration testing, security audits, incident response.",
                SalaryMin = 16000,
                SalaryMax = 24000,
                Location = "Warszawa",
                WorkType = WorkType.Onsite,
                CreatedAt = DateTime.UtcNow
            }),
            ("WebSolutions", new JobOffer
            {
                Title = "PHP Laravel Developer",
                Description = "Rozwijaj aplikacje webowe w Laravel. REST API, MySQL, Redis. Praca w małym, zgranym zespole.",
                SalaryMin = 9000,
                SalaryMax = 14000,
                Location = "Katowice",
                WorkType = WorkType.Remote,
                CreatedAt = DateTime.UtcNow.AddHours(-12)
            }),
            ("CrossPlatform Studios", new JobOffer
            {
                Title = "React Native Developer",
                Description = "Twórz aplikacje mobilne cross-platform. Znajomość React Native, Redux, TypeScript.",
                SalaryMin = 11000,
                SalaryMax = 16000,
                Location = "Poznań",
                WorkType = WorkType.Remote,
                CreatedAt = DateTime.UtcNow.AddHours(-10)
            }),
            ("DataOps Inc", new JobOffer
            {
                Title = "Database Administrator (PostgreSQL)",
                Description = "Zarządzaj bazami danych PostgreSQL. Performance tuning, backup strategies, high availability.",
                SalaryMin = 13000,
                SalaryMax = 19000,
                Location = "Wrocław",
                WorkType = WorkType.Hybrid,
                CreatedAt = DateTime.UtcNow.AddHours(-8)
            }),
            ("AI Innovations", new JobOffer
            {
                Title = "Machine Learning Engineer",
                Description = "Twórz modele ML i wdrażaj je do produkcji. Python, TensorFlow, PyTorch, MLOps.",
                SalaryMin = 17000,
                SalaryMax = 25000,
                Location = "Warszawa",
                WorkType = WorkType.Hybrid,
                CreatedAt = DateTime.UtcNow.AddHours(-6)
            }),
            ("CryptoTech", new JobOffer
            {
                Title = "Blockchain Developer",
                Description = "Rozwijaj aplikacje blockchain. Solidity, Ethereum, Smart Contracts. Znajomość Web3.js.",
                SalaryMin = 15000,
                SalaryMax = 23000,
                Location = "Remote",
                WorkType = WorkType.Remote,
                CreatedAt = DateTime.UtcNow.AddHours(-4)
            }),
            ("Infrastructure Masters", new JobOffer
            {
                Title = "System Administrator Linux",
                Description = "Administracja serwerami Linux. Bash scripting, monitoring, automation. Doświadczenie z Ansible mile widziane.",
                SalaryMin = 11000,
                SalaryMax = 16000,
                Location = "Gdańsk",
                WorkType = WorkType.Onsite,
                CreatedAt = DateTime.UtcNow.AddHours(-3)
            }),
            ("DataInsights Corp", new JobOffer
            {
                Title = "Business Intelligence Analyst",
                Description = "Twórz raporty i dashboardy w Power BI/Tableau. Analiza danych biznesowych, SQL, DAX.",
                SalaryMin = 10000,
                SalaryMax = 15000,
                Location = "Kraków",
                WorkType = WorkType.Hybrid,
                CreatedAt = DateTime.UtcNow.AddHours(-2)
            }),
            ("IoT Solutions", new JobOffer
            {
                Title = "Embedded Systems Developer",
                Description = "Programowanie systemów wbudowanych. C/C++, ARM, RTOS. Praca nad urządzeniami IoT.",
                SalaryMin = 12000,
                SalaryMax = 18000,
                Location = "Gliwice",
                WorkType = WorkType.Onsite,
                CreatedAt = DateTime.UtcNow.AddHours(-1)
            }),
            ("ProjectPro", new JobOffer
            {
                Title = "Technical Project Manager",
                Description = "Zarządzaj projektami IT od A do Z. Znajomość metodyk Agile i Waterfall. Doświadczenie z Jira, Confluence.",
                SalaryMin = 14000,
                SalaryMax = 20000,
                Location = "Warszawa",
                WorkType = WorkType.Hybrid,
                CreatedAt = DateTime.UtcNow.AddMinutes(-30)
            })
        };

        // oferty publiczne: aktywne, ważne od 14 do 60 dni (różne terminy)
        var now = DateTime.UtcNow;
        for (var i = 0; i < jobOffers.Count; i++)
        {
            jobOffers[i].Offer.Status = JobOfferStatus.Active;
            jobOffers[i].Offer.ExpiresAt = now.AddDays(14 + (i * 7) % 47);
        }

        // przykłady pozostałych stanów - niewidoczne na publicznej liście
        jobOffers.AddRange(new (string Company, JobOffer Offer)[]
        {
            ("CloudNative Labs", new JobOffer
            {
                Title = "Go Backend Developer",
                Description = "Szkic oferty: mikroserwisy w Go, gRPC, Kubernetes. Oferta w przygotowaniu - jeszcze nieopublikowana.",
                SalaryMin = 16000,
                SalaryMax = 23000,
                Location = "Warszawa",
                WorkType = WorkType.Remote,
                CreatedAt = now.AddHours(-5),
                Status = JobOfferStatus.Draft,
                ExpiresAt = now.AddDays(45)
            }),
            ("AppFactory", new JobOffer
            {
                Title = "iOS Developer (Swift)",
                Description = "Rekrutacja zakończona. Tworzenie aplikacji iOS w Swift i SwiftUI, współpraca z zespołem produktowym.",
                SalaryMin = 13000,
                SalaryMax = 19000,
                Location = "Kraków",
                WorkType = WorkType.Hybrid,
                CreatedAt = now.AddDays(-30),
                Status = JobOfferStatus.Closed,
                ExpiresAt = now.AddDays(10)
            }),
            ("TestHouse", new JobOffer
            {
                Title = "Junior Tester Manualny",
                Description = "Oferta wygasła. Testy manualne aplikacji webowych, raportowanie błędów w Jira, praca z dokumentacją.",
                SalaryMin = 6000,
                SalaryMax = 8000,
                Location = "Lublin",
                WorkType = WorkType.Onsite,
                CreatedAt = now.AddDays(-40),
                Status = JobOfferStatus.Active,
                ExpiresAt = now.AddDays(-3)
            })
        });

        // firmy tworzone z nazw ofert (zatwierdzone); lokalizacja firmy = lokalizacja jej pierwszej oferty
        foreach (var (companyName, offer) in jobOffers)
        {
            offer.Company = await GetOrCreateCompanyAsync(context, companyName, CompanyStatus.Approved, offer.Location, offer.CreatedAt);
        }

        await ApplySeedTaxonomy(context, jobOffers.Select(j => j.Offer).ToList());

        await context.JobOffers.AddRangeAsync(jobOffers.Select(j => j.Offer));
        await context.SaveChangesAsync();
    }

    // Kategorie demo; tworzone tylko, gdy tabela jest pusta (admin może je potem zmieniać i usuwać). Zwraca true, gdy je dodano.
    private static async Task<bool> SeedCategories(ApplicationDbContext context)
    {
        if (await context.Categories.AnyAsync())
        {
            return false;
        }

        context.Categories.AddRange(SeedCategoryList.Select((c, index) => new Category
        {
            Name = c.Name,
            Slug = c.Slug,
            SortOrder = (index + 1) * 10
        }));
        await context.SaveChangesAsync();
        return true;
    }

    // Uzupełnia taksonomię ofert demo istniejących w bazie sprzed etapu 5 (oferty bez kategorii, poziomu i umiejętności,
    // dopasowane po tytule); oferty dodane przez użytkowników mają inne tytuły, a edytowane ręcznie są pomijane
    private static async Task BackfillSeedTaxonomy(ApplicationDbContext context)
    {
        var titles = SeedTaxonomies.Keys.ToList();
        var offers = await context.JobOffers
            .Where(j => titles.Contains(j.Title) && j.CategoryId == null && j.Seniority == null && !j.Skills.Any())
            .ToListAsync();

        await ApplySeedTaxonomy(context, offers);
        await context.SaveChangesAsync();
    }

    // Ustawia kategorię, poziom, typy umów i umiejętności ofert demo wg tytułu (nowe umiejętności są dodawane do kontekstu)
    private static async Task ApplySeedTaxonomy(ApplicationDbContext context, IReadOnlyCollection<JobOffer> offers)
    {
        var categories = await context.Categories.ToDictionaryAsync(c => c.Slug);
        var skills = new Dictionary<string, Skill>();

        foreach (var offer in offers)
        {
            if (!SeedTaxonomies.TryGetValue(offer.Title, out var taxonomy))
            {
                continue;
            }

            offer.CategoryId = categories.TryGetValue(taxonomy.CategorySlug, out var category) ? category.Id : null;
            offer.Seniority = taxonomy.Seniority;
            offer.ContractTypes = taxonomy.ContractTypes;
            offer.SalaryCurrency = Currency.PLN;
            offer.SalaryPeriod = SalaryPeriod.Month;

            foreach (var name in taxonomy.Skills)
            {
                var key = SkillNames.KeyOf(name);
                if (!skills.TryGetValue(key, out var skill))
                {
                    skill = await context.Skills.FirstOrDefaultAsync(s => s.NormalizedName == key)
                            ?? context.Skills.Local.FirstOrDefault(s => s.NormalizedName == key);
                    if (skill == null)
                    {
                        skill = new Skill { Name = name, NormalizedName = key };
                        context.Skills.Add(skill);
                    }

                    skills[key] = skill;
                }

                offer.Skills.Add(skill);
            }
        }
    }

    private sealed record SeedCategoryInfo(string Name, string Slug);

    private sealed record SeedTaxonomy(
        string CategorySlug, SeniorityLevel Seniority, ContractTypes ContractTypes, string[] Skills);

    private static readonly SeedCategoryInfo[] SeedCategoryList =
    [
        new("Programowanie", "programowanie"),
        new("DevOps i chmura", "devops-i-chmura"),
        new("Testowanie", "testowanie"),
        new("Analiza danych", "analiza-danych"),
        new("Design UX/UI", "design-ux-ui"),
        new("Zarządzanie projektami", "zarzadzanie-projektami"),
        new("Bezpieczeństwo IT", "bezpieczenstwo-it"),
        new("Wsparcie IT", "wsparcie-it")
    ];

    private const ContractTypes EmploymentOrB2B = ContractTypes.EmploymentContract | ContractTypes.B2B;
    private const ContractTypes EmploymentOrMandate = ContractTypes.EmploymentContract | ContractTypes.MandateContract;

    // taksonomia ofert demo wg tytułu (waluta PLN, widełki miesięczne)
    private static readonly Dictionary<string, SeedTaxonomy> SeedTaxonomies = new()
    {
        ["Senior .NET Developer"] = new("programowanie", SeniorityLevel.Senior, EmploymentOrB2B,
            ["C#", ".NET", "ASP.NET Core", "Entity Framework", "SQL", "Docker"]),
        ["Frontend Developer - Angular"] = new("programowanie", SeniorityLevel.Mid, EmploymentOrB2B,
            ["Angular", "TypeScript", "RxJS", "HTML", "CSS", "Git"]),
        ["Full Stack Developer (React + Node.js)"] = new("programowanie", SeniorityLevel.Mid, ContractTypes.B2B,
            ["React", "Node.js", "TypeScript", "JavaScript", "SQL"]),
        ["DevOps Engineer"] = new("devops-i-chmura", SeniorityLevel.Senior, EmploymentOrB2B,
            ["AWS", "Docker", "Kubernetes", "Terraform", "Linux", "CI/CD"]),
        ["Java Backend Developer"] = new("programowanie", SeniorityLevel.Senior, ContractTypes.EmploymentContract,
            ["Java", "Spring Boot", "Hibernate", "Microservices", "SQL"]),
        ["Mobile Developer - Flutter"] = new("programowanie", SeniorityLevel.Mid, EmploymentOrB2B,
            ["Flutter", "Dart", "Firebase", "Git"]),
        ["Python Data Engineer"] = new("analiza-danych", SeniorityLevel.Mid, EmploymentOrB2B,
            ["Python", "SQL", "Pandas", "Apache Airflow", "ETL"]),
        ["UI/UX Designer"] = new("design-ux-ui", SeniorityLevel.Mid, EmploymentOrMandate,
            ["Figma", "Adobe XD", "Prototyping", "User Research"]),
        ["QA Automation Engineer"] = new("testowanie", SeniorityLevel.Mid, EmploymentOrB2B,
            ["Selenium", "Cypress", "JavaScript", "CI/CD", "Agile"]),
        ["Scrum Master"] = new("zarzadzanie-projektami", SeniorityLevel.Senior, EmploymentOrB2B,
            ["Scrum", "Agile", "Jira", "Facilitation"]),
        ["Cybersecurity Specialist"] = new("bezpieczenstwo-it", SeniorityLevel.Senior, EmploymentOrB2B,
            ["Penetration Testing", "Security Audits", "Incident Response", "Linux"]),
        ["PHP Laravel Developer"] = new("programowanie", SeniorityLevel.Junior, EmploymentOrMandate,
            ["PHP", "Laravel", "MySQL", "Redis", "REST API"]),
        ["React Native Developer"] = new("programowanie", SeniorityLevel.Mid, ContractTypes.B2B,
            ["React Native", "TypeScript", "Redux", "JavaScript"]),
        ["Database Administrator (PostgreSQL)"] = new("devops-i-chmura", SeniorityLevel.Senior, EmploymentOrB2B,
            ["PostgreSQL", "SQL", "Performance Tuning", "Backup", "Linux"]),
        ["Machine Learning Engineer"] = new("analiza-danych", SeniorityLevel.Senior, ContractTypes.B2B,
            ["Python", "TensorFlow", "PyTorch", "MLOps", "Docker"]),
        ["Blockchain Developer"] = new("programowanie", SeniorityLevel.Mid, ContractTypes.B2B,
            ["Solidity", "Ethereum", "Smart Contracts", "JavaScript"]),
        ["System Administrator Linux"] = new("wsparcie-it", SeniorityLevel.Mid, ContractTypes.EmploymentContract,
            ["Linux", "Bash", "Ansible", "Monitoring"]),
        ["Business Intelligence Analyst"] = new("analiza-danych", SeniorityLevel.Junior, EmploymentOrMandate,
            ["Power BI", "Tableau", "SQL", "DAX"]),
        ["Embedded Systems Developer"] = new("programowanie", SeniorityLevel.Mid, ContractTypes.EmploymentContract,
            ["C", "C++", "ARM", "RTOS"]),
        ["Technical Project Manager"] = new("zarzadzanie-projektami", SeniorityLevel.Manager, EmploymentOrB2B,
            ["Agile", "Jira", "Confluence", "Project Management"]),
        ["Go Backend Developer"] = new("programowanie", SeniorityLevel.Senior, ContractTypes.B2B,
            ["Go", "gRPC", "Kubernetes", "Microservices"]),
        ["iOS Developer (Swift)"] = new("programowanie", SeniorityLevel.Mid, ContractTypes.EmploymentContract,
            ["Swift", "SwiftUI", "iOS"]),
        ["Junior Tester Manualny"] = new("testowanie", SeniorityLevel.Intern,
            ContractTypes.EmploymentContract | ContractTypes.Internship,
            ["Manual Testing", "Jira", "Test Documentation"])
    };

    // Konta demo pracodawców: zatwierdzonej firmy z ofertami i firmy oczekującej na weryfikację (bez ofert)
    private static async Task SeedEmployers(ApplicationDbContext context, UserManager<ApplicationUser> userManager)
    {
        var employers = new[]
        {
            new
            {
                Email = ApprovedEmployerEmail, FirstName = "Katarzyna", LastName = "Zielińska",
                Company = ApprovedEmployerCompany, Status = CompanyStatus.Approved, Location = "Warszawa"
            },
            new
            {
                Email = PendingEmployerEmail, FirstName = "Marek", LastName = "Lewandowski",
                Company = PendingEmployerCompany, Status = CompanyStatus.Pending, Location = "Rzeszów"
            }
        };

        foreach (var employerData in employers)
        {
            var existing = await userManager.FindByEmailAsync(employerData.Email);
            if (existing?.CompanyId != null)
            {
                continue;
            }

            var company = await GetOrCreateCompanyAsync(context, employerData.Company, employerData.Status, employerData.Location, DateTime.UtcNow);
            if (company.Id == 0)
            {
                await context.SaveChangesAsync();
            }

            if (existing != null)
            {
                // konto demo bez firmy (np. po cofnięciu i ponownym zastosowaniu migracji AddCompanies) - dopinamy je do firmy
                existing.CompanyId = company.Id;
                await userManager.UpdateAsync(existing);
                continue;
            }

            var employer = new ApplicationUser
            {
                UserName = employerData.Email,
                Email = employerData.Email,
                FirstName = employerData.FirstName,
                LastName = employerData.LastName,
                EmailConfirmed = true,
                CreatedAt = DateTime.UtcNow,
                CompanyId = company.Id
            };

            var result = await userManager.CreateAsync(employer, EmployerPassword);
            if (result.Succeeded)
            {
                await userManager.AddToRoleAsync(employer, Roles.Employer);
            }
        }
    }

    // Firma o danej nazwie (bez rozróżniania wielkości liter) albo nowa, jeszcze niezapisana
    private static async Task<Company> GetOrCreateCompanyAsync(
        ApplicationDbContext context, string name, CompanyStatus status, string? location, DateTime createdAt)
    {
        var normalized = CompanyNames.Normalize(name);
        var lowered = normalized.ToLower();

        var company = context.Companies.Local.FirstOrDefault(c => c.Name.ToLower() == lowered)
                      ?? await context.Companies.FirstOrDefaultAsync(c => c.Name.ToLower() == lowered);
        if (company != null)
        {
            return company;
        }

        company = new Company
        {
            Name = normalized,
            Location = location,
            Status = status,
            CreatedAt = createdAt,
            ReviewedAt = status == CompanyStatus.Pending ? null : createdAt
        };
        context.Companies.Add(company);
        return company;
    }
}
