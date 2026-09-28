import React from 'react';
import ReactDOM from 'react-dom/client';
import App from './App';
import './index.css';
import { registerSW } from 'virtual:pwa-register';

// Register Service Worker for Offline Caching
registerSW({
  immediate: true,
  onNeedRefresh() {
    console.info('New content available, ready to refresh.');
  },
  onOfflineReady() {
    console.info('App is ready to work offline.');
  },
});

ReactDOM.createRoot(document.getElementById('root')!).render(
  <React.StrictMode>
    <App />
  </React.StrictMode>
);
