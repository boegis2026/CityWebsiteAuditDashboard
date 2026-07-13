# CityWebsiteAuditDashboard

# City Website Audit Dashboard

A prototype ASP.NET Core MVC application for auditing websites by collecting HTTP information and storing scan results in a SQL Server database.

This project was created as a personal learning project inspired by my internship with the City of Los Angeles Bureau of Engineering GIS Division. During the internship I performed manual website audits by collecting website information and recording it in spreadsheets. I wanted to explore how much of that workflow could be automated while learning ASP.NET Core MVC, Entity Framework Core, and SQL Server.

---

## Features

- Scan any website by entering its URL.
- Retrieve HTTP status code.
- Measure response time.
- Retrieve the `Server` HTTP header.
- Retrieve the `X-Powered-By` HTTP header (when available).
- Resolve the website's IPv4 address.
- Store every scan in SQL Server.
- View previous scan history.
- Search website records.
- Sort scan results.
- Filter scan results.
- Pagination support.
- Adjustable page size.
- Rescan existing websites.

---

## Technologies Used

- ASP.NET Core MVC
- C#
- Entity Framework Core
- Microsoft SQL Server
- SQL Server LocalDB
- Bootstrap 5
- Visual Studio 2026

---

## Project Structure

```
Controllers/
Data/
Models/
ViewModels/
Views/
wwwroot/
Program.cs
appsettings.json
```

---

---

## How It Works

1. Enter a website URL.
2. Click **Scan**.
3. The application:
   - Sends an HTTP request.
   - Measures the response time.
   - Retrieves HTTP headers.
   - Resolves the IPv4 address.
4. The results are saved to SQL Server using Entity Framework Core.
5. The dashboard displays all previous scans.

---

## Future Improvements

Planned features include:

- WAVE accessibility API integration
- Accessibility error tracking
- Dashboard statistics
- CSV import/export
- Bulk website scanning
- Scheduled scans
- Historical response-time reporting
- Improved reporting and analytics

---

## Learning Goals

This project was built to strengthen my understanding of:

- ASP.NET Core MVC
- Entity Framework Core
- SQL Server
- MVC architecture
- HTTP requests
- Dependency Injection
- Bootstrap
- LINQ
- Git and GitHub

---

## Installation

1. Clone the repository.

```bash
git clone https://github.com/yourusername/CityWebsiteAuditDashboard.git
```

2. Open the solution in Visual Studio 2026.

3. Update the SQL Server connection string if necessary.

4. Apply the database.

5. Run the application.

---

## Disclaimer

This project is a personal educational prototype created to practice ASP.NET Core MVC and SQL Server development. It is not an official City of Los Angeles application.

---

## Author

Roger Grajeda

Computer Science Student

California State University, Fullerton
