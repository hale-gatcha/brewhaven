# Brew Haven Coffee Shop (Walk-In)

Open `BrewHaven.csproj` in Visual Studio 2026. Run `BrewHaven_Schema.sql` in SQL Server or MonsterASP SQL panel. Update `App.config` placeholders, build, and run.

The seed data uses CHANGE_ME password hashes. Start from SQL and use the application's user management form or replace each password hash using PasswordHasher's output. In test builds, login will require the matching PBKDF2 hash.

## Multi-tenant setup

Brew Haven runs one database per shop (tenant), plus a **MasterCRM** database that lists the shops.

| Database | Contents |
|---|---|
| MasterCRM | `dbo.Tenants`: shop name, server, database name and login of each tenant database (`Migrations/MasterCRM/001_CreateTenants.sql`) |
| Tenant databases | The full Brew Haven schema (`BrewHaven_Schema.sql` + `Migrations/`), one copy per shop |

`App.config` holds only the MasterCRM connection string. It is git-ignored: copy `App.example.config` to `App.config` and fill in the MasterCRM server, login and password.

On the login screen, pick a **Shop** (loaded from MasterCRM). The app then connects to that shop's database for the rest of the session.

**Adding a shop:** create a database, restore a Brew Haven `.bak` (or run the schema scripts) into it, then add a row to `dbo.Tenants` in MasterCRM.
