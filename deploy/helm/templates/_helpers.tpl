{{/* Expand the name of the chart. */}}
{{- define "comuki.name" -}}
{{- default .Chart.Name .Values.nameOverride | trunc 63 | trimSuffix "-" }}
{{- end }}

{{/* Fullname: release-name unless the chart/name is taken. */}}
{{- define "comuki.fullname" -}}
{{- if .Values.fullnameOverride }}
{{- .Values.fullnameOverride | trunc 63 | trimSuffix "-" }}
{{- else }}
{{- $name := default .Chart.Name .Values.nameOverride }}
{{- if contains $name .Release.Name }}
{{- .Release.Name | trunc 63 | trimSuffix "-" }}
{{- else }}
{{- printf "%s-%s" .Release.Name $name | trunc 63 | trimSuffix "-" }}
{{- end }}
{{- end }}
{{- end }}

{{/* Common labels. */}}
{{- define "comuki.labels" -}}
helm.sh/chart: {{ printf "%s-%s" .Chart.Name .Chart.Version | replace "+" "_" | quote }}
{{ include "comuki.selectorLabels" . }}
app.kubernetes.io/version: {{ .Chart.AppVersion | quote }}
app.kubernetes.io/managed-by: {{ .Release.Service | quote }}
{{- end }}

{{/* Selector labels. */}}
{{- define "comuki.selectorLabels" -}}
app.kubernetes.io/name: {{ include "comuki.name" . }}
app.kubernetes.io/instance: {{ .Release.Name }}
{{- end }}

{{/* ServiceAccount name. */}}
{{- define "comuki.serviceAccountName" -}}
{{- if .Values.serviceAccount.create }}
{{- default (include "comuki.fullname" .) .Values.serviceAccount.name }}
{{- else }}
{{- default "default" .Values.serviceAccount.name }}
{{- end }}
{{- end }}

{{/* Image reference (host/brain/migrator share one image). */}}
{{- define "comuki.image" -}}
{{- $tag := default .Chart.AppVersion .Values.image.tag }}
{{- printf "%s:%s" .Values.image.repository $tag }}
{{- end }}

{{/* Dashboard image reference. */}}
{{- define "comuki.dashboardImage" -}}
{{- $tag := default .Chart.AppVersion .Values.dashboard.image.tag }}
{{- printf "%s:%s" .Values.dashboard.image.repository $tag }}
{{- end }}

{{/* Effective secret name: existingSecret or chart-managed. */}}
{{- define "comuki.secretName" -}}
{{- default (printf "%s-secrets" (include "comuki.fullname" .)) .Values.existingSecret }}
{{- end }}

{{/* Postgres host. */}}
{{- define "comuki.postgresHost" -}}
{{- if .Values.postgresql.external.enabled }}
{{- required "postgresql.external.host is required" .Values.postgresql.external.host }}
{{- else }}
{{- printf "%s-postgres" (include "comuki.fullname" .) }}
{{- end }}
{{- end }}

{{/* Postgres port. */}}
{{- define "comuki.postgresPort" -}}
{{- if .Values.postgresql.external.enabled }}
{{- .Values.postgresql.external.port }}
{{- else }}
{{- 5432 }}
{{- end }}
{{- end }}

{{/* Postgres database. */}}
{{- define "comuki.postgresDatabase" -}}
{{- if .Values.postgresql.external.enabled }}
{{- .Values.postgresql.external.database }}
{{- else }}
{{- "comuki" }}
{{- end }}
{{- end }}

{{/* Postgres username. */}}
{{- define "comuki.postgresUsername" -}}
{{- if .Values.postgresql.external.enabled }}
{{- .Values.postgresql.external.username }}
{{- else }}
{{- "comuki" }}
{{- end }}
{{- end }}

{{/*
  COMUKI_DB connection string. The password is NOT embedded — the
  container defines COMUKI_POSTGRES_PASSWORD (from the Secret) BEFORE
  COMUKI_DB so Kubernetes dependent-env expansion fills $(...).
  Order matters: any pod using this string must define the password
  env var first.
*/}}
{{- define "comuki.dbConnectionString" -}}
Host={{ include "comuki.postgresHost" . }};Port={{ include "comuki.postgresPort" . }};Database={{ include "comuki.postgresDatabase" . }};Username={{ include "comuki.postgresUsername" . }};Password=$(COMUKI_POSTGRES_PASSWORD)
{{- end }}

{{/* Artifacts (MinIO/S3) endpoint. */}}
{{- define "comuki.artifactsEndpoint" -}}
{{- if .Values.artifacts.external.enabled }}
{{- required "artifacts.external.endpoint is required" .Values.artifacts.external.endpoint }}
{{- else }}
{{- printf "%s-minio:9000" (include "comuki.fullname" .) }}
{{- end }}
{{- end }}

{{/* Artifacts SSL flag. */}}
{{- define "comuki.artifactsUseSSL" -}}
{{- if .Values.artifacts.external.enabled }}
{{- .Values.artifacts.external.useSSL | ternary "true" "false" }}
{{- else }}
{{- "false" }}
{{- end }}
{{- end }}

{{/* Orchestrator gRPC URL the workers connect back to. */}}
{{- define "comuki.orchestratorGrpcUrl" -}}
{{- default (printf "http://%s:80" (include "comuki.fullname" .)) .Values.compute.orchestratorGrpcUrl }}
{{- end }}

{{/* Worker Jobs namespace (defaults to the release namespace). */}}
{{- define "comuki.workerNamespace" -}}
{{- default .Release.Namespace .Values.compute.kubernetes.namespace }}
{{- end }}
