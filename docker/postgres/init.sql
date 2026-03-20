-- Script eseguito da PostgreSQL al primo avvio del container.
-- Crea i database necessari se non esistono.

-- Database master (già creato da POSTGRES_DB=mysolutionhub_master)
-- Database tenant di sviluppo
SELECT 'CREATE DATABASE mysolutionhub_tenant001'
WHERE NOT EXISTS (
    SELECT FROM pg_database WHERE datname = 'mysolutionhub_tenant001'
)\gexec

-- Design-time database per EF Core migrations
SELECT 'CREATE DATABASE mysolutionhub_tenant_design'
WHERE NOT EXISTS (
    SELECT FROM pg_database WHERE datname = 'mysolutionhub_tenant_design'
)\gexec
