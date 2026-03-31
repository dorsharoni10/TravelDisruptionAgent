import { Header } from './components/Header';
import { HomePage } from './pages/HomePage';

function App() {
  return (
    <div className="flex min-h-screen flex-col bg-slate-50">
      <Header />
      <main className="flex-1">
        <HomePage />
      </main>
    </div>
  );
}

export default App;
