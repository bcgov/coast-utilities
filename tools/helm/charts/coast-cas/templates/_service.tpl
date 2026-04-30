# service template
{{- define "service.tpl" -}}
{{- if .Values.port }}
kind: Service
apiVersion: v1
metadata:
  name: {{ .name }}-svc
  labels: {{ .labels | nindent 4 }}
spec:
  selector:
    name: {{ .name }}
  ports:
    - name: {{ printf "%s-%s" (.Values.port | toString) .Values.protocol }}
      port: {{ .Values.port }}
      protocol: {{ .Values.protocol | upper }}
      targetPort: {{ .Values.targetPort }}
  type: ClusterIP
{{- end -}}
{{- end -}}
