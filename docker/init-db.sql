-- Provisionamento local da API Geo dedicada.
--
-- Este script roda como POSTGRES_USER (superusuário) no primeiro boot do
-- container, via /docker-entrypoint-initdb.d.

-- Banco usado pelo Keycloak local do compose.
CREATE DATABASE keycloak;

-- Banco isolado do Geo, read-mostly, com PostGIS (ADR-0090/0091).
-- A extensão postgis exige superusuário; por isso ela é criada aqui. A migration
-- do Geo também emite CREATE EXTENSION IF NOT EXISTS postgis, que vira no-op
-- quando a extensão já está presente.
CREATE ROLE uniplus_geo_app LOGIN PASSWORD 'uniplus_dev';
CREATE DATABASE uniplus_geo OWNER uniplus_geo_app;

\c uniplus_geo
CREATE EXTENSION IF NOT EXISTS "uuid-ossp";
CREATE EXTENSION IF NOT EXISTS "pg_trgm";
CREATE EXTENSION IF NOT EXISTS unaccent;
CREATE EXTENSION IF NOT EXISTS btree_gist;
CREATE EXTENSION IF NOT EXISTS postgis;
