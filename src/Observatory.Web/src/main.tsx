import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import App from './App';
import { AppErrorBoundary } from './components/Common';
import './styles.css';

const root = document.getElementById('root');
if (!root) throw new Error('Contenitore root mancante.');
createRoot(root).render(<StrictMode><AppErrorBoundary><App /></AppErrorBoundary></StrictMode>);
