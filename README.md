# 🏁 Race — RESTful API

A REST API for the **Race** event management system, built with **ASP.NET Core 8.0 Web API**, **Entity Framework Core (Code-First)**, **SQLite**, **ASP.NET Core Identity**, and **JWT** authentication.

The API powers both the **Organiser** and **Participant** sides of the system:

- **Organisers** can create, update, delete events; create distance categories; view all enrolments; and capture finish results.
- **Participants** can browse events, view categories, enrol into a category, cancel enrolments, and view their own enrolment and performance history.
- Public endpoints allow anyone to browse upcoming events, categories, and event leaderboards.


---

## ⚙️ Prerequisites

- **.NET 8 SDK** — https://dotnet.microsoft.com/download
- **EF Core CLI tools** — install once:
  ```bash
  dotnet tool install --global dotnet-ef --version 8.0.10

  git clone https://github.com/<your-username>/Race.git
cd Race

dotnet restore

dotnet ef database update --project Race

dotnet run --project Race

Now listening on: https://localhost:7080

https://localhost:7080/swagger

Example
POST /api/auth/register
{
  "name": "Thandi Mokoena",
  "email": "thandi@example.com",
  "password": "Password1",
  "role": "Organiser",
  "contactNumber": "0821234567"
}

Exampe response
{
  "token": "eyJhbGciOi...",
  "user": { "id": "…", "name": "Thandi Mokoena", "email": "…", "role": "Organiser" }
}
