# deployment template
{{- define "deployment.tpl" }}
kind: Deployment
apiVersion: apps/v1
metadata:
  name: {{ .name }}
  labels: {{ .labels | nindent 4 }}
  annotations:
    image.openshift.io/triggers: '[{"from":{"kind":"ImageStreamTag","name":"{{ base .Values.image.name }}:{{ .Values.image.tag }}","namespace":"{{ .Values.image.triggerNamespace }}"},"fieldPath":"spec.template.spec.containers[?(@.name==\"{{ .name }}\")].image","pause":"false"}]'
spec:
  replicas: {{ .Values.scaling.minReplicas }}
  revisionHistoryLimit: 10
  strategy:
    type: RollingUpdate
    rollingUpdate:
      maxUnavailable: 50%
      maxSurge: 50%
  selector:
    matchLabels:
      name: {{ .name }}
  template:
    metadata:
      name: {{ .name }}
      labels:
        name: {{ .name }}
        role: {{ .Values.role }}
      {{- if or .Values.secrets .Values.env }}
      annotations:
        {{- if gt (len .Values.env) 0 }}
        checksum/configmap: {{ .Values.env | toYaml | sha256sum }}
        {{- end }}
        {{- if gt (len .Values.secrets) 0 }}
        checksum/secret: {{ .Values.secrets | toYaml | sha256sum }}
        {{- end }}
      {{- end }}
    spec:
      containers:
        - name: {{ .name }}
          image: {{ .Values.image.name }}:{{ .Values.image.tag }}
          imagePullPolicy: Always
          securityContext:
            allowPrivilegeEscalation: false
          resources: {{ .Values.resources | toYaml | nindent 12 }}

          {{- if or .Values.env .Values.secrets }}
          envFrom:
            {{- if .Values.env }}
            - configMapRef:
                name: {{ .name }}-configmap
            {{- end }}
            {{- if .Values.secrets }}
            - secretRef:
                name: {{ .name }}-secret
            {{- end }}
          {{- end }}

          {{- if .Values.volumeMounts }}
          volumeMounts:
            {{ .Values.volumeMounts | toYaml | nindent 12 }}
          {{- end }}

          {{- if .Values.port }}
          ports:
            - containerPort: {{ .Values.port }}
              protocol: {{ .Values.protocol | default "TCP" | upper }}
          {{- end }}

          {{- if .Values.livenessProbe }}
          livenessProbe: {{ .Values.livenessProbe | toYaml | nindent 12 }}
          {{- end }}

          {{- if .Values.readinessProbe }}
          readinessProbe: {{ .Values.readinessProbe | toYaml | nindent 12 }}
          {{- end }}

          {{- if .Values.startupProbe }}
          startupProbe: {{ .Values.startupProbe | toYaml | nindent 12 }}
          {{- end }}

      dnsPolicy: ClusterFirst
      restartPolicy: Always
      terminationGracePeriodSeconds: 30

      {{- if .Values.volumes }}
      volumes:
        {{ tpl (.Values.volumes | toYaml) $ | nindent 8 }}
      {{- end }}
{{- end }}
