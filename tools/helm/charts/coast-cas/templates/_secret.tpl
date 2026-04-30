# secrets template
{{- define "secret.tpl" }}
{{- if .Values.secrets }}
kind: Secret
apiVersion: v1
metadata:
  name: {{ .name }}-secret
  labels: {{ .labels | nindent 4 }}
data:
  {{- range $key, $value := .Values.secrets }}
  {{ $key }}: {{ $value | toString | b64enc | quote }}
  {{- end -}}
{{- end -}}
{{- end -}}
