# configmap template
{{- define "configmap.tpl" }}
{{- if .Values.env }}
kind: ConfigMap
apiVersion: v1
metadata:
  name: {{ .name }}-configmap
  labels: {{ .labels | nindent 4 }}
data:
  {{- range $key, $value := .Values.env }}
  {{ $key }}: {{ $value | toString | quote }}
  {{- end }}
{{- end -}}
{{- end -}}
