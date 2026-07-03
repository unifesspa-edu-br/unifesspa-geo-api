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

-- Banco de staging da DNE (issue #12): recebe os 15 dumps brutos (Correios/IBGE,
-- gerados via Navicat) via `psql -f` sem nenhuma reescrita de schema — os dumps
-- qualificam "public"."tbl_cep_..." explicitamente, então só um "public" NATIVO
-- (banco próprio, não um schema secundário do uniplus_geo) os recebe de forma
-- transparente. Role própria (não uniplus_geo_app) por menor privilégio: o app
-- do domínio nunca precisa enxergar o dataset bruto, e vice-versa.
CREATE ROLE uniplus_geo_staging_app LOGIN PASSWORD 'uniplus_dev';
CREATE DATABASE uniplus_geo_staging OWNER uniplus_geo_staging_app;
