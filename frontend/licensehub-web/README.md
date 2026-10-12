# LicenseHub Web

Base Angular 22.2.2 con Material, TypeScript estricto, Signals, RxJS y PWA.

Desde este directorio:

```text
npm ci
npm run lint
npm run build
npm start
```

La vista local se abre en http://127.0.0.1:4200. El proxy solo consulta las sondas del backend en el puerto 5002. La pantalla muestra disponibilidad real; no implementa todavía login, licencias ni pagos. El modo oscuro y la actualización automática cada 30 segundos funcionan en la pantalla inicial.

El build de producción activa el service worker para recursos estáticos. API, health y OpenAPI quedan fuera de la navegación offline. Los iconos PNG se rasterizaron desde public/licensehub-mark.svg. No hay credenciales privadas en este cliente.

Se verificaron build, TypeScript, formato y vistas de escritorio/móvil en navegador. Todavía no se configuraron tests frontend unitarios o end-to-end ni se probó instalación PWA en dispositivos reales. Para instrucciones completas, ver ../../docs/installation.md.
